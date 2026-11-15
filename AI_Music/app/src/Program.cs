using System;
using System.IO;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--auto")
            {
                Environment.Exit(AutoRunner.Run(args[1]));
                return;
            }
            if (args.Length >= 1 && args[0] == "--ui-test")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = new MainForm();
                form.StartUiTest();
                Application.Run(form);
                Environment.Exit(form.UiTestResult);
                return;
            }
            if (args.Length >= 1 && args[0] == "--selftest")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int code = 0;
                try
                {
                    using (MainForm form = new MainForm())
                    {
                        form.CreateControl();
                    }
                    string quoteDetails;
                    if (!Paths.SelfTestQuoting(out quoteDetails))
                    {
                        WriteSelftestError(quoteDetails);
                        code = 1;
                    }
                }
                catch (Exception ex)
                {
                    WriteSelftestError(ex.ToString());
                    code = 1;
                }
                Environment.Exit(code);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void WriteSelftestError(string message)
        {
            try
            {
                string dir = Paths.TempDir;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "selftest_error.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + message,
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
            Console.Error.WriteLine(message);
        }
    }
}
