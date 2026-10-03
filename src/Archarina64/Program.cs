using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SharpOcarina.Shared;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SharpOcarina
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static string ApplicationTitle = "Archarina64";
        public static int ApplicationVersion = 0x1671;

        public static MainForm MF;
        public static bool QuitProgram = false;

        public static string KeyboardLayout = "";

        public static bool exportMode = false;

        [STAThread]
        static void Main(string[] args)
        {
            ArcharinaWorkspace.Initialize();
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs error)
            { ArcharinaWorkspace.LogError(error.ExceptionObject as Exception); };
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs error)
            {
                ArcharinaWorkspace.LogError(error.Exception);
                MessageBox.Show(error.Exception.Message, "Archarina64 error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            bool z64romtasks = false;
            string toml = "";

            if (args.Length > 0)
            {
                foreach (string arg in args)
                {
                    if (arg.ToLower() == "--z64romtasks") z64romtasks = true;
                    else if (arg.ToLower() == "--export") exportMode = true;
                    else if (Path.GetExtension(arg).ToLower() == ".toml")
                    {
                        toml = arg;
                    }
                }
            }
            if ((z64romtasks || exportMode) && toml != "")
            {
                if (!File.Exists(toml))
                {
                    MessageBox.Show("z64project.toml not found", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            if(z64romtasks && toml != "")
            {
                FaroresPlugin.AddLinkAnimations(Path.GetDirectoryName(toml), true);
                FaroresPlugin.ConvertAllIncPngFiles(Path.GetDirectoryName(toml));
                FaroresPlugin.CustomDMAEntries(Path.GetDirectoryName(toml), true);
                FaroresPlugin.BuildFunctionNamesArray(Path.GetDirectoryName(toml));
                return;
            }
            if(exportMode && toml == "")
            {
                exportMode = false;
            }

            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                KeyboardLayout = GetKeyboardLayout();
            }
            catch (Exception ex)
            {
                //MessageBox.Show("Could not detect keyboard layout, so using QWERTY");
                KeyboardLayout = "QWERTY";
            }

            

            MF = new MainForm(Array.IndexOf(args, "--sandbox-rom") >= 0 ? new string[0] : args);



            MF.FormClosed += new FormClosedEventHandler(MF_FormClosed);
            ArcharinaUiTheme.Apply(MF);
            MF.Show();
            int sandboxArgument = Array.IndexOf(args, "--sandbox-rom");
            if (sandboxArgument >= 0 && sandboxArgument + 1 < args.Length)
                MF.BeginInvoke(new Action(async () =>
                {
                    try { await MF.OpenSandboxRomAsync(args[sandboxArgument + 1]); }
                    catch (Exception error) { MessageBox.Show(MF, error.Message, "ROM library", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }));


#if DEBUG
            if (Array.IndexOf(args, "--console") >= 0) ConsoleWindow.CreateConsole();
#else
           // ApplicationTitle = MF.GetType().Namespace + GetVerString(ApplicationVersion);
#endif
            // MF.GetType().Namespace + " 1.3";
            
            MF.BringToFront();
            do
            {
                
                MF.ProgramMainLoop();
                Application.DoEvents();

                System.Threading.Thread.Sleep(2);
            }
            while (QuitProgram == false);
        }

        public static string GetKeyboardLayout()
        {
            string layout = "";
            uint MAPVK_VSC_TO_VK = 0x01;

            uint[] QWERTYCodes =
                {
                0x00000010,
                0x00000011,
                0x00000012,
                0x00000013,
                0x00000014,
                0x00000015,
            };

            foreach (var code in QWERTYCodes)
            {
                var vk = MapVirtualKey(code, MAPVK_VSC_TO_VK);
                var val = KeyInterop.KeyFromVirtualKey((int)vk);
                layout += val;
            }

            if (layout.EndsWith("PYF"))
                return "DVORAK";

            return layout;
        }

        static void MF_FormClosed(object sender, FormClosedEventArgs e)
        {
            QuitProgram = true;
        }

        public static string GetVerString(int Version)
        {
            string VerString = "";

            VerString = " v" +
                (Version >> 8).ToString() + "." +
                ((Version & 0xF0) >> 4).ToString();

            if ((Version & 0xF) != 0)
                VerString += "." + (Version & 0xF).ToString();

            // VerString += " (Beta)";

            return VerString;
        }
    }
}
