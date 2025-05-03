using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace Crypto
{
    public static class ContextMenuManager
    {
        private const string MenuName = "CryptoHash";
        private const string MenuText = "Вычислить хэш (Crypto)";
        private const string TempIconName = "iconCrypto.ico";

        public static bool RemoveContextMenuEntry()
        {
            bool success = true;

            try
            {
                // Удаление для всех пользователей
                try
                {
                    Registry.ClassesRoot.DeleteSubKeyTree($"*\\shell\\{MenuName}", false);
                }
                catch { success = false; }

                // Удаление для текущего пользователя
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\*\\shell\\{MenuName}", false);
                }
                catch { success = false; }

                // Удаление временной иконки
                try
                {
                    string tempIconPath = Path.Combine(Path.GetTempPath(), TempIconName);
                    if (File.Exists(tempIconPath))
                        File.Delete(tempIconPath);
                }
                catch { /* Игнорируем ошибки удаления файла */ }

                return success;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsContextMenuInstalled()
        {
            // Проверка для текущего пользователя
            using (var key = Registry.CurrentUser.OpenSubKey($"Software\\Classes\\*\\shell\\{MenuName}\\command"))
            {
                if (key != null) return true;
            }

            // Проверка для всех пользователей
            using (var key = Registry.ClassesRoot.OpenSubKey($"*\\shell\\{MenuName}\\command"))
            {
                return key != null;
            }
        }

        public static bool AddContextMenuForCurrentUser()
        {
            try
            {
                string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string iconPath = ExtractIconToTempFile() ?? $"\"{appPath}\",0";

                using (var key = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\*\\shell\\{MenuName}"))
                {
                    key.SetValue("", MenuText);
                    key.SetValue("Icon", iconPath);
                }

                using (var cmdKey = Registry.CurrentUser.CreateSubKey($"Software\\Classes\\*\\shell\\{MenuName}\\command"))
                {
                    cmdKey.SetValue("", $"\"{appPath}\" \"%1\"");
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении в контекстное меню: {ex.Message}",
                              "Ошибка",
                              MessageBoxButton.OK,
                              MessageBoxImage.Error);
                return false;
            }
        }

        public static bool AddContextMenuForAllUsers()
        {
            try
            {
                string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string iconPath = ExtractIconToTempFile() ?? $"\"{appPath}\",0";

                using (var key = Registry.ClassesRoot.CreateSubKey($"*\\shell\\{MenuName}"))
                {
                    key.SetValue("", MenuText);
                    key.SetValue("Icon", iconPath);
                }

                using (var cmdKey = Registry.ClassesRoot.CreateSubKey($"*\\shell\\{MenuName}\\command"))
                {
                    cmdKey.SetValue("", $"\"{appPath}\" \"%1\"");
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Требуются права администратора для добавления меню для всех пользователей",
                              "Ошибка прав доступа",
                              MessageBoxButton.OK,
                              MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static string ExtractIconToTempFile()
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), TempIconName);

                // Если иконка уже существует
                if (File.Exists(tempPath))
                    return tempPath;

                // Получаем иконку из ресурсов
                var iconStream = Application.GetResourceStream(
                    new Uri("pack://application:,,,/Resources/iconCrypto.ico"))?.Stream;

                if (iconStream == null)
                    return null!;

                using (var fileStream = File.Create(tempPath))
                {
                    iconStream.CopyTo(fileStream);
                }

                return tempPath;
            }
            catch
            {
                return null!;
            }
        }
    }
}