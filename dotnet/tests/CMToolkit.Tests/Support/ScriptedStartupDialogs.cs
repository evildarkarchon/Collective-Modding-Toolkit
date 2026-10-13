using System.Text.Json;
using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Support;

/// <summary>
/// <see cref="IStartupDialogs"/> answered from a scenario's <c>host.dialogs</c> script, in call order, recording every
/// call in the shape the driver's <c>DialogScript</c> records the reference's <c>messagebox</c>/<c>filedialog</c> calls,
/// so the C# flow's dialogs compare against the golden's <c>dialogs</c> array.
/// </summary>
/// <remarks>
/// Like the driver, an asking dialog with no answer left, or with the next answer meant for another function, fails the
/// test; <see cref="Unused"/> lists answers nothing asked for. <c>&lt;ROOT&gt;</c> in a string answer is expanded with
/// <paramref name="expand"/>.
/// </remarks>
public sealed class ScriptedStartupDialogs(IReadOnlyList<DialogAnswer> answers, Func<string, string> expand) : IStartupDialogs
{
    private readonly Queue<DialogAnswer> _answers = new(answers);

    /// <summary>Every dialog shown, as the driver records it: <c>module</c>, <c>function</c>, <c>args</c>, <c>kwargs</c>.</summary>
    public JsonArray Calls { get; } = [];

    /// <summary>Scripted answers no dialog asked for.</summary>
    public IReadOnlyCollection<DialogAnswer> Unused => _answers;

    /// <inheritdoc/>
    public Task<bool> AskYesNoAsync(string title, string message)
    {
        Record("messagebox", "askyesno", new JsonArray(title, message), new JsonObject());
        return Task.FromResult(Next("askyesno").GetBoolean());
    }

    /// <inheritdoc/>
    public Task<string> AskOpenFileNameAsync(string title, string fileTypeName, string fileTypePattern)
    {
        // filedialog.askopenfilename(title=..., filetypes=((name, pattern),)): keyword arguments only.
        Record("filedialog", "askopenfilename", new JsonArray(), new JsonObject
        {
            ["filetypes"] = new JsonArray(new JsonArray(fileTypeName, fileTypePattern)),
            ["title"] = title,
        });
        return Task.FromResult(expand(Next("askopenfilename").GetString()!));
    }

    /// <inheritdoc/>
    public Task ShowErrorAsync(string title, string message)
    {
        Record("messagebox", "showerror", new JsonArray(title, message), new JsonObject());
        return Task.CompletedTask;
    }

    private void Record(string module, string function, JsonArray args, JsonObject kwargs)
        => Calls.Add(new JsonObject { ["args"] = args, ["function"] = function, ["kwargs"] = kwargs, ["module"] = module });

    private JsonElement Next(string function)
    {
        if (!_answers.TryDequeue(out var answer))
        {
            throw new InvalidOperationException($"Unscripted dialog: {function} has no answer left in host.dialogs.");
        }

        if (answer.Function != function)
        {
            throw new InvalidOperationException($"Dialog out of order: {function} was called, but the next answer is for {answer.Function}.");
        }

        return answer.Answer;
    }
}
