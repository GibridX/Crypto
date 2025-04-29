using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Crypto
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public event Action<int> ProgressChanged = delegate { };
        public event Action<string> StatusUpdated = delegate { };
        private DateTime startTime;
        private string? _calculatedHash;
        private bool _isHashCalculated = false;


        public MainWindow()
        {
            InitializeComponent();
            ProgressChanged += (progress) => Dispatcher.Invoke(() =>
            {
                FileProgressBar.Value = progress;
                txtProgressBar.Text = $"{progress}%";
            });
            StatusUpdated += (status) => Dispatcher.Invoke(() => txtfileStatus.Text = status);
            txtUserHash.IsEnabled = false;
            CompareButton.IsEnabled = false;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "Файлы All files (*.*)|*.*",
                Title = "Выберите файл"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                txtFilePath.Text = openFileDialog.FileName;
                ResetComparisonUI();
            }
        }

        private void ResetComparisonUI()
        {
            _isHashCalculated = false;
            _calculatedHash = null;
            txtUserHash.IsEnabled = false;
            CompareButton.IsEnabled = false;
            txtResults.Text = string.Empty;
            txtUserHash.Text = string.Empty;
            FileProgressBar.Value = 0;
            txtProgressBar.Text = string.Empty;
            txtfileStatus.Text = "Ожидание...";
        }

        private async void CalculateHash_Click(object sender, RoutedEventArgs e)
        {
            string filePath = txtFilePath.Text;

            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("Выберите файл.");
                return;
            }
            if (HashAlgorithmBox.SelectedItem is not ComboBoxItem selectedItem)
            {
                MessageBox.Show("Выберите алгоритм");
                return;
            }
            
            string algorithmName = selectedItem.Content.ToString()!;
            using HashAlgorithm hashAlgorithm = algorithmName switch
            {
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                _ => throw new ArgumentException("Неверный алгоритм")
            };
            try
            {
                _calculatedHash = await CalculateFileHashAsync(filePath, hashAlgorithm);
                txtResults.Text = $"Хэш: {_calculatedHash}";
                _isHashCalculated = true;
                txtUserHash.IsEnabled = true;
                CompareButton.IsEnabled = true;
                txtUserHash.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private async Task<string> CalculateFileHashAsync(string filePath, HashAlgorithm hashAlgorithm)
        {
            startTime = DateTime.Now;
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] buffer = new byte[8192];
            int bytesRead;
            long totalBytesRead = 0;
            long fileSize = stream.Length;

            hashAlgorithm.Initialize();

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalBytesRead += bytesRead;

                int progress = (int)((double)totalBytesRead / fileSize * 100);
                ProgressChanged.Invoke(progress);

                if (totalBytesRead > 0)
                {
                    double speed = totalBytesRead / (DateTime.Now - startTime).TotalSeconds;
                    double remainingTime = (fileSize - totalBytesRead) / speed;
                    string timeLeft = TimeSpan.FromSeconds(remainingTime).ToString(@"mm\:ss");
                    StatusUpdated.Invoke($"{FormatBytes(totalBytesRead)} / {FormatBytes(fileSize)} байт осталось | Осталось: {timeLeft}");
                }
            }

            hashAlgorithm.TransformFinalBlock([], 0, 0);
            byte[] hashBytes = hashAlgorithm.Hash!;
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }
        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "байт", "КБ", "МБ", "ГБ", "ТБ" };
            int suffixIndex = 0;
            double size = bytes;
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        private void CompareHash_Click(object sender, RoutedEventArgs e)
        {
            if (!_isHashCalculated || string.IsNullOrEmpty(_calculatedHash))
            {
                MessageBox.Show("Сначала вычислите хэш файла.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string userHash = txtUserHash.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(userHash))
            {
                MessageBox.Show("Введите хэш для сравнения.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isMatch = string.Equals(_calculatedHash, userHash, StringComparison.OrdinalIgnoreCase);

            txtResults.Text = $"Сравнение хэша:\n" +
                             $"Файл: {Path.GetFileName(txtFilePath.Text)}\n" +
                             $"Хэш файла: {_calculatedHash}\n" +
                             $"Введённый хэш: {userHash}\n" +
                             $"Результат: {(isMatch ? "СОВПАДАЕТ" : "НЕ СОВПАДАЕТ")}\n\n";
        }
    }
}