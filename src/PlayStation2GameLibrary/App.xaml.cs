using System.Windows;

namespace PlayStation2GameLibrary;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // GUI smoke tests call Run(window) with an explicit MainWindow. In that case,
        // do not create another application window here.
        if (MainWindow is not null) return;

#if ADMIN_BUILD
        MainWindow = new AdminMainWindow();
#else
        MainWindow = new MainWindow();
#endif
        MainWindow.Show();
    }
}
