using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;

namespace ChatClient
{
    public partial class MainWindow : Window
    {
        private TcpClient client;
        private NetworkStream stream;
        private string username;
        private DispatcherTimer typingTimer;
        private bool isTyping = false;
        private string chatHistoryFilePath;

        public MainWindow()
        {
            InitializeComponent();
            this.Title = "Chat App Kelompok 8";

            typingTimer = new DispatcherTimer();
            typingTimer.Interval = TimeSpan.FromSeconds(1.5);
            typingTimer.Tick += (s, a) => {
                Task.Run(() => SendSpecialMessageAsync("/typing-end"));
                isTyping = false;
                typingTimer.Stop();
            };

            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string chatHistoryFolder = Path.Combine(appDataFolder, "ChatClientApp");

            if (!Directory.Exists(chatHistoryFolder))
            {
                Directory.CreateDirectory(chatHistoryFolder);
            }
            chatHistoryFilePath = Path.Combine(chatHistoryFolder, "chat_history.log");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Atur tema default setelah semua komponen dimuat.
            DarkModeRadio.IsChecked = true;
            LoadChatHistory();
        }

        private void LoadChatHistory()
        {
            if (File.Exists(chatHistoryFilePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(chatHistoryFilePath);
                    foreach (string line in lines)
                    {
                        lstChat.Items.Add(line);
                    }
                    lstChat.ScrollIntoView(lstChat.Items[lstChat.Items.Count - 1]);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Gagal memuat riwayat chat: {ex.Message}");
                }
            }
        }

        private void SaveMessageToFile(string message)
        {
            try
            {
                File.AppendAllText(chatHistoryFilePath, message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gagal menyimpan pesan ke file: {ex.Message}");
            }
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(txtUsername.Text))
                {
                    MessageBox.Show("Username tidak boleh kosong.");
                    return;
                }
                client = new TcpClient();
                await client.ConnectAsync(txtIP.Text, int.Parse(txtPort.Text));
                stream = client.GetStream();
                username = txtUsername.Text;
                byte[] usernameBytes = Encoding.UTF8.GetBytes($"/user {username}");
                await stream.WriteAsync(usernameBytes, 0, usernameBytes.Length);
                lstChat.Items.Add($"[SYSTEM] Terhubung sebagai {username}.");
                UpdateUiOnConnect();
                _ = Task.Run(() => ReceiveMessagesAsync());
            }
            catch (SocketException)
            {
                MessageBox.Show("Server tidak dapat dijangkau.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            byte[] buffer = new byte[1024];
            while (client != null && client.Connected)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        Dispatcher.Invoke(() =>
                        {
                            string formattedMessage = "";
                            if (message.StartsWith("/users "))
                            {
                                // Menerima dan memperbarui daftar pengguna
                                string[] users = message.Substring("/users ".Length).Split(',');
                                lstUsers.Items.Clear();
                                foreach (var user in users)
                                {
                                    if (!string.IsNullOrWhiteSpace(user))
                                    {
                                        lstUsers.Items.Add(user);
                                    }
                                }
                            }
                            else if (message.StartsWith("/system "))
                            {
                                formattedMessage = $"[SYSTEM] {message.Substring("/system ".Length)}";
                                lstChat.Items.Add(formattedMessage);
                                SaveMessageToFile(formattedMessage);
                            }
                            else if (message.StartsWith("/typing-start "))
                            {
                                // Menampilkan indikator typing
                                TypingIndicator.Text = $"{message.Substring("/typing-start ".Length)} sedang mengetik...";
                            }
                            else if (message.StartsWith("/typing-end "))
                            {
                                // Menghilangkan indikator typing
                                TypingIndicator.Text = "";
                            }
                            else
                            {
                                formattedMessage = $"[{DateTime.Now.ToShortTimeString()}] {message}";
                                lstChat.Items.Add(formattedMessage);
                                SaveMessageToFile(formattedMessage);
                            }
                            lstChat.ScrollIntoView(lstChat.Items[lstChat.Items.Count - 1]);
                        });
                    }
                    else
                    {
                        break;
                    }
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception)
                {
                    break;
                }
            }
            if (client != null)
            {
                Dispatcher.Invoke(UpdateUiOnDisconnect);
            }
        }

        private void UpdateUiOnConnect()
        {
            btnConnect.IsEnabled = false;
            btnDisconnect.IsEnabled = true;
            btnSend.IsEnabled = true;
            txtIP.IsEnabled = false;
            txtPort.IsEnabled = false;
            txtUsername.IsEnabled = false;
        }

        private void UpdateUiOnDisconnect()
        {
            btnConnect.IsEnabled = true;
            btnDisconnect.IsEnabled = false;
            btnSend.IsEnabled = false;
            txtIP.IsEnabled = true;
            txtPort.IsEnabled = true;
            txtUsername.IsEnabled = true;
            lstChat.Items.Add("[SYSTEM] Koneksi terputus.");
        }

        private async void btnSend_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void txtMessage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SendMessageAsync();
                typingTimer.Stop();
                await SendSpecialMessageAsync("/typing-end");
                isTyping = false;
            }
        }

        private async void txtMessage_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (client != null && client.Connected)
            {
                if (!isTyping && !string.IsNullOrWhiteSpace(txtMessage.Text))
                {
                    // Kirim sinyal start typing hanya jika ada teks
                    await SendSpecialMessageAsync("/typing-start");
                    isTyping = true;
                }
            }
            // Reset timer: jika user mengetik lagi dalam 1.5s, timer diset ulang
            typingTimer.Stop();
            typingTimer.Start();

            // Hentikan typing jika TextBox kosong
            if (string.IsNullOrWhiteSpace(txtMessage.Text) && isTyping)
            {
                typingTimer.Stop();
                await SendSpecialMessageAsync("/typing-end");
                isTyping = false;
            }
        }

        private async Task SendSpecialMessageAsync(string message)
        {
            try
            {
                if (stream != null && stream.CanWrite && client.Connected)
                {
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saat mengirim pesan khusus: {ex.Message}");
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(txtMessage.Text))
                return;
            try
            {
                string messageToSend = txtMessage.Text;
                if (stream != null && stream.CanWrite && client.Connected)
                {
                    byte[] messageBytes = Encoding.UTF8.GetBytes(messageToSend);
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                }

                if (!messageToSend.StartsWith("/w "))
                {
                    string formattedMessage = $"[{DateTime.Now.ToShortTimeString()}] Anda: {messageToSend}";
                    lstChat.Items.Add(formattedMessage);
                    SaveMessageToFile(formattedMessage);
                    lstChat.ScrollIntoView(lstChat.Items[lstChat.Items.Count - 1]);
                }
                txtMessage.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saat mengirim pesan: {ex.Message}");
            }
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (client != null && client.Connected)
            {
                client.Close();
                if (stream != null)
                {
                    stream.Dispose();
                }
                if (client != null)
                {
                    client.Dispose();
                }
            }
        }

        // Metode untuk menangani klik tombol PM
        private void PmButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                string targetUser = button.CommandParameter as string;
                if (!string.IsNullOrEmpty(targetUser))
                {
                    txtMessage.Text = $"/w {targetUser} ";
                    txtMessage.Focus();
                    txtMessage.CaretIndex = txtMessage.Text.Length;
                }
            }
        }

        private void LightModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            ApplyLightMode();
        }

        private void DarkModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            ApplyDarkMode();
        }

        private void ApplyLightMode()
        {
            MainGrid.Background = Brushes.WhiteSmoke;
            SidebarBorder.Background = Brushes.LightGray;
            IpTextBlock.Foreground = Brushes.Black;
            PortTextBlock.Foreground = Brushes.Black;
            UsernameTextBlock.Foreground = Brushes.Black;
            ThemeTextBlock.Foreground = Brushes.Black;
            LightModeRadio.Foreground = Brushes.Black;
            DarkModeRadio.Foreground = Brushes.Black;
            lstChat.Background = Brushes.White;
            lstChat.Foreground = Brushes.Black;
            lstUsers.Background = Brushes.LightGray;
            lstUsers.Foreground = Brushes.Black;
            txtMessage.Background = Brushes.White;
            txtMessage.Foreground = Brushes.Black;
            UserListHeader.Foreground = Brushes.Black;
            TypingIndicator.Foreground = Brushes.Black;
            btnSend.Background = Brushes.CadetBlue;
            btnSend.BorderBrush = Brushes.CadetBlue;
            btnConnect.Background = Brushes.CadetBlue;
            btnConnect.BorderBrush = Brushes.CadetBlue;
            btnDisconnect.Background = Brushes.IndianRed;
            btnDisconnect.BorderBrush = Brushes.IndianRed;
        }

        private void ApplyDarkMode()
        {
            BrushConverter converter = new BrushConverter();

            MainGrid.Background = (SolidColorBrush)converter.ConvertFrom("#36393F");
            SidebarBorder.Background = (SolidColorBrush)converter.ConvertFrom("#2F3136");
            IpTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE");
            PortTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE");
            UsernameTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE");
            ThemeTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE");
            LightModeRadio.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE");
            DarkModeRadio.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE");
            lstChat.Background = (SolidColorBrush)converter.ConvertFrom("#36393F");
            lstChat.Foreground = (SolidColorBrush)converter.ConvertFrom("#DCDDDE");
            lstUsers.Background = (SolidColorBrush)converter.ConvertFrom("#2F3136");
            lstUsers.Foreground = (SolidColorBrush)converter.ConvertFrom("#DCDDDE");
            txtMessage.Background = (SolidColorBrush)converter.ConvertFrom("#40444B");
            txtMessage.Foreground = (SolidColorBrush)converter.ConvertFrom("#DCDDDE");
            UserListHeader.Foreground = (SolidColorBrush)converter.ConvertFrom("#8E9297");
            TypingIndicator.Foreground = (SolidColorBrush)converter.ConvertFrom("#7289DA");
            btnSend.Background = (SolidColorBrush)converter.ConvertFrom("#7289DA");
            btnSend.BorderBrush = (SolidColorBrush)converter.ConvertFrom("#7289DA");
            btnConnect.Background = (SolidColorBrush)converter.ConvertFrom("#7289DA");
            btnConnect.BorderBrush = (SolidColorBrush)converter.ConvertFrom("#7289DA");
            btnDisconnect.Background = (SolidColorBrush)converter.ConvertFrom("#FF5733");
            btnDisconnect.BorderBrush = (SolidColorBrush)converter.ConvertFrom("#FF5733");
        }
    }
}