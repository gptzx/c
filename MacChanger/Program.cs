using System;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace MacChanger
{
    internal static class Program
    {
        public const string AppTitle = "MAC 주소 변경 유틸리티";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 매니페스트(requireAdministrator)가 UAC 승격을 강제하지만, 매니페스트가 제거된 채 실행된 경우를 대비해 한 번 더 확인한다 (확인이 실패하면 관리자가 아닌 것으로 본다).
            bool admin;
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                admin = false;
            }
            if (!admin)
            {
                MessageBox.Show(
                    "이 프로그램은 관리자 권한이 필요합니다.\r\n" +
                    "실행 파일을 마우스 오른쪽 버튼으로 클릭한 뒤 '관리자 권한으로 실행'을 선택하세요.",
                    AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            Application.Run(new MainForm());
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show("처리되지 않은 오류가 발생했습니다:\r\n" + e.Exception.Message,
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            MessageBox.Show("처리되지 않은 오류가 발생했습니다:\r\n" + (ex != null ? ex.Message : Convert.ToString(e.ExceptionObject, null)),
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
