using AracKiralama.Business.Services;

namespace AracKiralama.UI.Infrastructure;

/// <summary>Saves a rental's printable contract where the user chooses; shared by the rental lists.</summary>
internal sealed class ContractExport(IContractService contracts, UserSession session)
{
    /// <summary>The save dialog; replaced in tests so no window opens.</summary>
    public ISaveFilePrompt? SaveFiles { get; set; }

    /// <summary>Where the file is written; replaced in tests.</summary>
    public Func<string, byte[], Task> WriteFile { get; set; } = (path, content) => ExportFileWriter.WriteAsync(path, content);

    /// <summary>The operation every contract button of a list runs through.</summary>
    public ExclusiveOperation Operation { get; } = new();

    /// <summary>
    /// Creates the contract and saves it after asking where; returns whether a file was written. Ignored
    /// while another save of the list runs, and every contract button stays disabled meanwhile.
    /// </summary>
    public async Task<bool> SaveAsync(Form owner, IUserPrompts prompts, int rentalId, IReadOnlyList<Button> buttons)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(buttons);

        var written = false;
        await owner.RunExclusiveAsync(
            Operation,
            async () =>
            {
                var file = await contracts.CreateAsync(session, rentalId);
                var prompt = SaveFiles ?? new SaveFileDialogPrompt(owner);
                if (prompt.AskSavePath(file.SuggestedName, ExportFilters.Html) is not { } path)
                {
                    return;
                }

                await WriteFile(path, file.Content);
                written = true;
                prompts.Inform("Sözleşme kaydedildi. Dosyayı tarayıcıda açıp yazdırabilirsiniz.");
            },
            [.. buttons]);
        return written;
    }
}
