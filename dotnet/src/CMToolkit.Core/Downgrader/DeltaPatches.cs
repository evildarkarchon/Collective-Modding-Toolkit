using System.Net;
using CMToolkit.Core.Updates;
using VCDiff.Decoders;
using VCDiff.Includes;

namespace CMToolkit.Core.Downgrader;

/// <summary>
/// The Downgrader's Delta Patches: where they're downloaded from (NET-3) and how one is applied (<c>pyxdelta.decode</c>,
/// DG-6). A patch turns one version of a file into the other; its name is its direction plus the file's name, such as
/// <c>OG-to-NG-Fallout4.exe.xdelta</c>.
/// </summary>
public static class DeltaPatches
{
    /// <summary>
    /// The folder the patches are downloaded from: this fork's byte-identical mirror of the reference's
    /// <c>delta-patches</c> release. The reference used <c>PATCH_URL_BASE</c> =
    /// <c>https://github.com/wxMichael/Collective-Modding-Toolkit/releases/download/delta-patches/</c>
    /// (<c>downgrader.py:51</c>). Where the patches are hosted is a deployment detail, not Behaviour Parity: the bytes
    /// are the same and the CRC tables check every output. The hosting decision and why there's no fallback or setting
    /// are in issue #16 ("Decide where the Downgrader's delta patches are hosted"); if the app's releases move to another
    /// repository, the mirror and this constant move with them.
    /// </summary>
    public const string UrlBase = "https://github.com/evildarkarchon/Collective-Modding-Toolkit/releases/download/delta-patches/";

    /// <summary>The Old-Gen to Next-Gen direction prefix.</summary>
    public const string OldGenToNextGen = "OG-to-NG-";

    /// <summary>The Next-Gen to Old-Gen direction prefix.</summary>
    public const string NextGenToOldGen = "NG-to-OG-";

    /// <summary>
    /// The largest file a patch may rebuild. The real patches rebuild at most about 80 MB; VCDiff refuses a target
    /// larger than its limit, which xdelta3 has no equivalent of, so the limit is set well past any game file.
    /// </summary>
    private const int MaxTargetFileSize = 1 << 30;

    /// <summary>
    /// <c>timeout=10</c> on the download: how long one connect, read or write may take, not the whole download (T-14),
    /// which can be tens of megabytes.
    /// </summary>
    public static TimeSpan ReadTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary><c>f"{PATCH_URL_BASE}{patch_direction}{file_path.name}.xdelta"</c>.</summary>
    /// <param name="direction"><see cref="OldGenToNextGen"/> or <see cref="NextGenToOldGen"/>.</param>
    /// <param name="fileName">The patched file's name, without its folder.</param>
    public static string Url(string direction, string fileName) => $"{UrlBase}{direction}{fileName}.xdelta";

    /// <summary>
    /// The real network stack for downloads: <see cref="UpdateCheck.CreateHandler"/>'s per-read timeouts at
    /// <see cref="ReadTimeout"/>, redirects followed (GitHub's release assets redirect to its object store), and no
    /// automatic decompression, so the bytes saved and the <c>Content-Length</c> the progress divides by are the ones
    /// the server sent.
    /// </summary>
    public static SocketsHttpHandler CreateHandler()
    {
        var handler = UpdateCheck.CreateHandler(ReadTimeout);
        // requests decoded gzip itself while keeping the encoded Content-Length; GitHub serves the patches unencoded,
        // and asking for no encoding keeps HttpClient from dropping the header that the progress needs.
        handler.AutomaticDecompression = DecompressionMethods.None;
        return handler;
    }

    /// <summary>
    /// <c>pyxdelta.decode(infile, patchfile, outfile)</c>: rebuilds <paramref name="output"/> from
    /// <paramref name="source"/> with the patch <paramref name="patch"/>.
    /// </summary>
    /// <remarks>
    /// Like xdelta3, it never throws: every failure, a missing or unreadable file included, is false. The output is
    /// written one verified window at a time, as xdelta3 writes it: no file at all when the first window fails, and the
    /// windows before a later failure left behind. An existing output file is replaced.
    /// </remarks>
    /// <returns>Whether the whole target was rebuilt.</returns>
    public static bool Decode(string source, string patch, string output)
    {
        PendingOutput? target = null;
        try
        {
            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            target = new PendingOutput(output);
            using var deltaStream = new CommitOnReadStream(new FileStream(patch, FileMode.Open, FileAccess.Read, FileShare.Read), target);
            using var decoder = new VcDecoder(sourceStream, deltaStream, target, MaxTargetFileSize);
            var result = decoder.Decode(out _);
            if (result != VCDiffResult.SUCCESS)
            {
                return false;
            }

            target.Commit();
            return true;
        }
        catch (Exception)
        {
            // xdelta3 reports every failure, a corrupt patch or an unreadable file alike, as a nonzero status that
            // pyxdelta returns as False; VCDiff throws for some of them (malformed input, I/O), so all mean false here.
            return false;
        }
        finally
        {
            target?.Dispose();
        }
    }

    /// <summary>
    /// The decoder's output, held back one write at a time. VCDiff writes a window, then checks its Adler-32; xdelta3
    /// checks first and writes only good windows. So a write is kept pending until the decoder moves on to the next
    /// window (it reads the patch again, see <see cref="CommitOnReadStream"/>) or finishes, and dropped if it fails
    /// instead. The file is created by the first commit, so a decode that fails in its first window leaves none.
    /// </summary>
    private sealed class PendingOutput(string path) : Stream
    {
        private readonly MemoryStream _pending = new();
        private FileStream? _file;

        /// <summary>False: the decoder only writes its output.</summary>
        public override bool CanRead => false;

        /// <summary>False: the output is written front to back, so the decoder never seeks it.</summary>
        public override bool CanSeek => false;

        /// <summary>True.</summary>
        public override bool CanWrite => true;

        /// <summary>Not supported: the stream can't seek.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override long Length => throw new NotSupportedException();

        /// <summary>Not supported: the stream can't seek.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>Writes the pending bytes to the file, creating it (replacing any old one) on the first commit.</summary>
        public void Commit()
        {
            if (_pending.Length == 0)
            {
                return;
            }

            _file ??= new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _pending.Position = 0;
            _pending.CopyTo(_file);
            _pending.SetLength(0);
        }

        /// <summary>See <see cref="Write(ReadOnlySpan{byte})"/>.</summary>
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        /// <summary>Commits the write before this one, then holds this one back until it is known to be good.</summary>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            // A new window's write means the previous one was verified: VCDiff checks a window before decoding the next.
            Commit();
            _pending.Write(buffer);
        }

        /// <summary>Does nothing; see the comment inside.</summary>
        public override void Flush()
        {
            // Nothing to do: pending bytes reach the file only when committed, and the file is flushed when disposed.
        }

        /// <summary>Not supported: the stream is write-only.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>Not supported: the stream can't seek.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>Not supported: the stream can't seek.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>
        /// Closes the file, if a commit created it, and drops the write still pending: a decode that didn't finish never
        /// committed its last window, so a failed one leaves only the windows it verified.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file?.Dispose();
                _pending.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The patch, read through: any read the decoder makes after a write commits that write, because VCDiff reads the
    /// next window's header only once the last window's checksum has passed. Seeks pass through.
    /// </summary>
    private sealed class CommitOnReadStream(FileStream inner, PendingOutput output) : Stream
    {
        /// <summary>True.</summary>
        public override bool CanRead => true;

        /// <summary>The patch file's: seeks pass through.</summary>
        public override bool CanSeek => inner.CanSeek;

        /// <summary>False: the patch is only read.</summary>
        public override bool CanWrite => false;

        /// <summary>The patch file's length.</summary>
        public override long Length => inner.Length;

        /// <summary>The patch file's position; setting it seeks without committing, as a seek reads nothing.</summary>
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        /// <summary>See <see cref="Read(Span{byte})"/>.</summary>
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <summary>Commits the output's pending write, then reads the patch.</summary>
        public override int Read(Span<byte> buffer)
        {
            output.Commit();
            return inner.Read(buffer);
        }

        /// <summary>Commits the output's pending write, then reads one byte of the patch.</summary>
        public override int ReadByte()
        {
            output.Commit();
            return inner.ReadByte();
        }

        /// <summary>Seeks the patch file, committing nothing.</summary>
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        /// <summary>Flushes the patch file, which has nothing to flush as it is only read.</summary>
        public override void Flush() => inner.Flush();

        /// <summary>Not supported: the patch is only read.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Not supported: the patch is only read.</summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>Closes the patch file, which this stream owns.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
