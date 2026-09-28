using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Utilities;
using Mesen.ViewModels;
using Mesen.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mesen
{
	public class App : Application
	{
		public static bool ShowConfigWindow { get; set; }

		private static List<string> _pendingOpenedFiles = new();
		private static Action<string[]>? _openedFilesHandler;

		public override void Initialize()
		{
			if(Design.IsDesignMode || ShowConfigWindow) {
				RequestedThemeVariant = ThemeVariant.Light;
			} else {
				RequestedThemeVariant = ConfigManager.Config.Preferences.Theme == MesenTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
			}

			Dispatcher.UIThread.UnhandledException += (s, e) => {
				MesenMsgBox.ShowException(e.Exception);
				e.Handled = true;
			};

#if DEBUG
			this.AttachDeveloperTools();
#endif
			AvaloniaXamlLoader.Load(this);
			ResourceHelper.LoadResources();
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				if(ShowConfigWindow) {
					new PreferencesConfig().InitializeFontDefaults();
					desktop.MainWindow = new SetupWizardWindow();
				} else {
					//Test if the core can be loaded, and display an error message popup if not
					try {
						EmuApi.TestDll();
					} catch(Exception ex) {
						bool sdlMissing = ex.Message.Contains("SDL2", StringComparison.InvariantCultureIgnoreCase);

						string errorMessage;
						if(sdlMissing) {
							errorMessage = ResourceHelper.GetMessage("UnableToStartMissingSdl", ex.Message);
						} else {
							errorMessage = ResourceHelper.GetMessage("UnableToStartMissingDependencies", ex.Message + Environment.NewLine + ex.StackTrace);
						}
						MessageBox.Show(null, errorMessage, "MesenCE", MessageBoxButtons.OK, MessageBoxIcon.Error, out MessageBox msgbox);
						desktop.MainWindow = msgbox;
						base.OnFrameworkInitializationCompleted();
						return;
					}

					try {
						desktop.MainWindow = new MainWindow();
					} catch {
						//Something broke when trying to load the main window, the settings file might be invalid/broken, try to reset them
						Configuration.BackupSettings(ConfigManager.ConfigFile);
						ConfigManager.ResetSettings(false);
						desktop.MainWindow = new MainWindow();
					}

					if(TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatableLifetime) {
						activatableLifetime.Activated += OnActivated;
					}
				}
			}
			base.OnFrameworkInitializationCompleted();
		}

		private void OnActivated(object? sender, ActivatedEventArgs e)
		{
			//On macOS, files opened via Finder/Dock (double-click, "Open With", drag & drop on icon, etc.)
			//are not passed on the command line, they are sent to the app via this event instead
			if(e is FileActivatedEventArgs fileArgs) {
				string[] files = fileArgs.Files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
				if(files.Length > 0) {
					Dispatcher.UIThread.Post(() => {
						if(_openedFilesHandler != null) {
							_openedFilesHandler(files);
						} else {
							//Emulator isn't done initializing yet, load the files once it is
							_pendingOpenedFiles.AddRange(files);
						}
					});
				}
			}
		}

		public static void SetOpenedFilesHandler(Action<string[]> handler)
		{
			Dispatcher.UIThread.VerifyAccess();
			_openedFilesHandler = handler;
			if(_pendingOpenedFiles.Count > 0) {
				handler(_pendingOpenedFiles.ToArray());
				_pendingOpenedFiles.Clear();
			}
		}
	}
}
