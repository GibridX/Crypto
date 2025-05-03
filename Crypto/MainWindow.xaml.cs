using Microsoft.Win32;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        private DateTime _lastUiUpdate = DateTime.MinValue;
        private string? _calculatedHash;
        private bool _isHashCalculated = false;
        private bool _ThemeChanged = false;
        private bool _isOperationRunnning = false;
        private CancellationTokenSource? _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            ApplyTheme("light.xaml");
            ProgressChanged += (progress) => Dispatcher.Invoke(() =>
            {
                FileProgressBar.Value = progress;
                txtProgressBar.Text = $"{progress}%";
            });
            StatusUpdated += (status) => Dispatcher.Invoke(() => txtfileStatus.Text = status);
            txtUserHash.IsEnabled = false;
            CompareButton.IsEnabled = false;
            UpdateContextMenuStatus();
        }

        #region UI/UX элементы
        private void txtFilePath_PreviewDragOver(object sender, DragEventArgs e)
        {
            if(e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                txtFilePath.Background = Brushes.LightGreen;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void txtFilePath_PreviewDragLeave(object sender, DragEventArgs e)
        {
            txtFilePath.Background = null;
        }

        private void txtFilePath_Drop(object sender, DragEventArgs e)
        {
            txtFilePath.Background = null;
            if(e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    txtFilePath.Text = files[0];
                }
            }
        }

        private void AddToContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if(ContextMenuManager.AddContextMenuForCurrentUser())
            {
                MessageBox.Show("Приложение добавлено в контекстное меню", "Успех", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateContextMenuStatus();
            }
        }

        private void RemoveFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if(ContextMenuManager.RemoveContextMenuEntry())
            {
                MessageBox.Show("Приложение удалено из контекстного меню", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            UpdateContextMenuStatus();
        }

        private void UpdateContextMenuStatus()
        {
            contextMenuStatusText.Text = ContextMenuManager.IsContextMenuInstalled()
                ? "Контекстное меню: установлено"
                : "Контекстное меню: не установлено";
        }

        #endregion

        #region Дополнительные методы (Внешние)
        public void SetInitialFile(string filePath)
        {
            txtFilePath.Text = filePath;
        }
        #endregion

        #region Дополнительные методы (Состояния)
        private void ResetOperationState(Button button)
        {
            _isOperationRunnning = false;
            button.Content = "Вычислить хэш";
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        #endregion

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

            if(!File.Exists(txtFilePath.Text))
            {
                MessageBox.Show("Файл не существует или путь неверен", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var button = (Button)sender;

            if(_isOperationRunnning)
            {
                _cancellationTokenSource?.Cancel();
                return;
            }

            _isOperationRunnning = true;
            button.Content = "Отменить";
            StatusUpdated.Invoke("Идёт вычисление...");

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
                "MD5" => MD5.Create(),
                "SHA1" => SHA1.Create(),
                "SHA256" => SHA256.Create(),
                "SHA384" => SHA384.Create(),
                "SHA512" => SHA512.Create(),
                _ => throw new ArgumentException("Неверный алгоритм")
            };

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _calculatedHash = await Task.Run(() => CalculateFileHashAsync(filePath, hashAlgorithm, _cancellationTokenSource.Token));
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                txtResults.Text = $"Хэш: {_calculatedHash}";
                _isHashCalculated = true;
                txtUserHash.IsEnabled = true;
                CompareButton.IsEnabled = true;
                txtUserHash.Focus();
            }
            catch(OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                StatusUpdated.Invoke("Операция отменена");
                FileProgressBar.Value = 0;
                txtProgressBar.Text = string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
            finally
            {
                ResetOperationState(button);
            }
        }

        private async Task<string> CalculateFileHashAsync(string filePath, HashAlgorithm hashAlgorithm, CancellationToken cancellationToken)
        {
            startTime = DateTime.Now;
            _lastUiUpdate = DateTime.MinValue;

            var buffer = ArrayPool<byte>.Shared.Rent(4 *  1024 * 1024);
            try
            {
                using var stream = new FileStream(filePath, 
                    FileMode.Open, 
                    FileAccess.Read, 
                    FileShare.Read, 
                    bufferSize: 4096, 
                    options: FileOptions.SequentialScan | FileOptions.Asynchronous
                );

                int bytesRead;
                long totalBytesRead = 0;
                long fileSize = stream.Length;
                int lastReportedProgress = -1;
                double smoothedSpeed = 0;
                string lastTimeLeft = "";

                hashAlgorithm.Initialize();

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalBytesRead += bytesRead;

                    int progress = (int)((double)totalBytesRead / fileSize * 100);

                    if ((DateTime.Now - _lastUiUpdate).TotalMinutes >= 50 || 
                        progress == 100 || Math.Abs(progress - lastReportedProgress) >= 5)
                    {
                        double currentSpeed = totalBytesRead / (DateTime.Now - startTime).TotalSeconds;
                        smoothedSpeed = smoothedSpeed == 0 ? currentSpeed : (smoothedSpeed * 0.7 + currentSpeed * 0.3);
                        double remainingTime = (fileSize - totalBytesRead) / smoothedSpeed;
                        string timeleft = TimeSpan.FromSeconds(remainingTime).ToString(@"mm\:ss");

                        if(timeleft != lastTimeLeft || progress != lastReportedProgress)
                        {
                            StatusUpdated.Invoke($"{FormatBytes(totalBytesRead)} / {FormatBytes(fileSize)} | Осталось: {timeleft}");
                            ProgressChanged.Invoke(progress);
                            _lastUiUpdate = DateTime.Now;
                            lastTimeLeft = timeleft;
                            lastReportedProgress = progress;
                        }
                    }
                }

                hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                byte[] hashBytes = hashAlgorithm.Hash ?? throw new InvalidOperationException("Не удалось вычислить хэш");
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            catch(OperationCanceledException)
            {
                StatusUpdated.Invoke("Операция отменена");
                ProgressChanged.Invoke(0);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
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

            string cleanUserHash = Regex.Replace(userHash, "[^0-9a-fA-F]", "").ToLower();
            string cleanCalculatedHash = _calculatedHash.ToLower();

            bool isMatch = string.Equals(cleanCalculatedHash, cleanUserHash, StringComparison.OrdinalIgnoreCase);

            txtResults.Text = $"Сравнение хэша:\n" +
                             $"Файл: {Path.GetFileName(txtFilePath.Text)}\n" +
                             $"Хэш файла: {_calculatedHash}\n" +
                             $"Введённый хэш: {userHash}\n" +
                             $"Результат: {(isMatch ? "Совпадает" : "Не совпадает")}\n\n";
        }

        private void ThemeSwitch_Click(object sender, RoutedEventArgs e)
        {
            _ThemeChanged = !_ThemeChanged;

            if (_ThemeChanged)
            {
                ApplyTheme("dark.xaml");
                ThemeTxt.Text = "Текущая тема: Тёмная";
            }
            else
            {
                ApplyTheme("light.xaml");
                ThemeTxt.Text = "Текущая тема: Светлая";
            }
        }

        private void ApplyTheme(string themeFile)
        {
            Resources.MergedDictionaries.Clear();

            var themeUri = new Uri(themeFile, UriKind.Relative);
            var themeDict = (ResourceDictionary)Application.LoadComponent(themeUri);
            Resources.MergedDictionaries.Add(themeDict);
        }
    }
}