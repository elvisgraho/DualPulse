using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using DualPulse.App.Services;
using DualPulse.App.Views;

namespace DualPulse.App.Platform;

// Avalonia's save and open dialogs, shown over DualPulse's window, for profile files.
public sealed class FileDialogs : IFileDialogs
{
	private static readonly FilePickerFileType ProfileFiles = new("DualPulse profile") { Patterns = ["*" + ProfileFile.Extension] };

	public async Task<string?> PickSaveAsync(string suggestedName)
	{
		if (Storage() is not { } storage)
		{
			return null;
		}
		IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Export profile",
			SuggestedFileName = suggestedName,
			DefaultExtension = "json",
			FileTypeChoices = [ProfileFiles]
		});
		return file?.TryGetLocalPath();
	}

	public async Task<string?> PickOpenAsync()
	{
		if (Storage() is not { } storage)
		{
			return null;
		}
		IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Import profile",
			AllowMultiple = false,
			FileTypeFilter = [ProfileFiles, FilePickerFileTypes.All]
		});
		return files.Count > 0 ? files[0].TryGetLocalPath() : null;
	}

	// Through DualPulse's own window (the app keeps it itself; the desktop lifetime's MainWindow stays unset).
	private static IStorageProvider? Storage() =>
		(Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.OfType<ShellWindow>().FirstOrDefault()?.StorageProvider;
}
