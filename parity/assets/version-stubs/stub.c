/*
 * The code of every version-resourced stub binary. The Overview only reads a binary's version resource and its CRC32
 * (OVW-B2), and nothing ever runs or loads a stub, so the exe entry point just returns. DLL stubs are linked with
 * /NOENTRY and keep none of this code.
 *
 * No CRT: the stubs stay a couple of KB, and /Brepro keeps every rebuild byte-identical.
 */

int __stdcall StubMain(void)
{
    return 0;
}
