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
        private TcpClient? client;       
        private NetworkStream? stream;   
        private string username = string.Empty;
        private DispatcherTimer typingTimer;
        private bool isTyping = false;
        private readonly string chatHistoryFilePath; 

        public MainWindow()
        {
            InitializeComponent();
            this.Title = "Chat App Kelompok 7";

            typingTimer = new DispatcherTimer(); 
            typingTimer.Interval = TimeSpan.FromSeconds(1.5);
            typingTimer.Tick += (s, a) => {
                Task.Run(() => SendSpecialMessageAsync("/typing-end"));
                isTyping = false;
                typingTimer.Stop();
            };

            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var chatHistoryFolder = Path.Combine(appDataFolder, "ChatClientApp");

            if (!Directory.Exists(chatHistoryFolder))
            {
                Directory.CreateDirectory(chatHistoryFolder);
            }
            chatHistoryFilePath = Path.Combine(chatHistoryFolder, "chat_history.log");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
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
                    lstChat.ScrollIntoView(lstChat.Items[^1]); 
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

                var usernameBytes = Encoding.UTF8.GetBytes($"/user {username}"); 
                await stream.WriteAsync(usernameBytes); 

                lstChat.Items.Add($"[SYSTEM] Terhubung sebagai {username}.");
                UpdateUiOnConnect();
                _ = Task.Run(ReceiveMessagesAsync);
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
            var buffer = new byte[1024]; 
            while (client is not null && client.Connected) 
            {
                try
                {
                    int bytesRead = await stream!.ReadAsync(buffer); 
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        Dispatcher.Invoke(() =>
                        {
                            string formattedMessage = "";
                            if (message.StartsWith("/users "))
                            {
                                var users = message[7..].Split(',');
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
                                formattedMessage = $"[SYSTEM] {message[8..]}"; 
                                lstChat.Items.Add(formattedMessage);
                                SaveMessageToFile(formattedMessage);
                            }
                            else if (message.StartsWith("/typing-start "))
                            {
                                TypingIndicator.Text = $"{message[14..]} sedang mengetik..."; 
                            }
                            else if (message.StartsWith("/typing-end "))
                            {
                                TypingIndicator.Text = "";
                            }
                            else
                            {
                                formattedMessage = $"[{DateTime.Now.ToShortTimeString()}] {message}";
                                lstChat.Items.Add(formattedMessage);
                                SaveMessageToFile(formattedMessage);
                            }
                            lstChat.ScrollIntoView(lstChat.Items[^1]); 
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
            if (client is not null)
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

        private async void btnSend_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

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

        private async void txtMessage_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (client is not null && client.Connected)
            {
                if (!isTyping && !string.IsNullOrWhiteSpace(txtMessage.Text))
                {
                    await SendSpecialMessageAsync("/typing-start");
                    isTyping = true;
                }
            }
            typingTimer.Stop();
            typingTimer.Start();

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
                if (stream?.CanWrite == true && client!.Connected)
                {
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(messageBytes);
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
                if (stream?.CanWrite == true && client!.Connected)
                {
                    var messageBytes = Encoding.UTF8.GetBytes(messageToSend);
                    await stream.WriteAsync(messageBytes);
                }

                if (!messageToSend.StartsWith("/w "))
                {
                    var formattedMessage = $"[{DateTime.Now.ToShortTimeString()}] Anda: {messageToSend}";
                    lstChat.Items.Add(formattedMessage);
                    SaveMessageToFile(formattedMessage);
                    lstChat.ScrollIntoView(lstChat.Items[^1]);
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
            client?.Close();
            stream?.Dispose();
            client?.Dispose();
        }

        private void PmButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: string targetUser } button) 
            {
                txtMessage.Text = $"/w {targetUser} ";
                txtMessage.Focus();
                txtMessage.CaretIndex = txtMessage.Text.Length;
            }
        }

        private void LightModeRadio_Checked(object sender, RoutedEventArgs e) => ApplyLightMode();
        private void DarkModeRadio_Checked(object sender, RoutedEventArgs e) => ApplyDarkMode();

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
            var converter = new BrushConverter();

            MainGrid.Background = (SolidColorBrush)converter.ConvertFrom("#36393F")!;
            SidebarBorder.Background = (SolidColorBrush)converter.ConvertFrom("#2F3136")!;
            IpTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE")!;
            PortTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE")!;
            UsernameTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE")!;
            ThemeTextBlock.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE")!;
            LightModeRadio.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE")!;
            DarkModeRadio.Foreground = (SolidColorBrush)converter.ConvertFrom("#B9BBBE")!;
            lstChat.Background = (SolidColorBrush)converter.ConvertFrom("#36393F")!;
            lstChat.Foreground = (SolidColorBrush)converter.ConvertFrom("#DCDDDE")!;
            lstUsers.Background = (SolidColorBrush)converter.ConvertFrom("#2F3136")!;
            lstUsers.Foreground = (SolidColorBrush)converter.ConvertFrom("#DCDDDE")!;
            txtMessage.Background = (SolidColorBrush)converter.ConvertFrom("#40444B")!;
            txtMessage.Foreground = (SolidColorBrush)converter.ConvertFrom("#DCDDDE")!;
            UserListHeader.Foreground = (SolidColorBrush)converter.ConvertFrom("#8E9297")!;
            TypingIndicator.Foreground = (SolidColorBrush)converter.ConvertFrom("#7289DA")!;
            btnSend.Background = (SolidColorBrush)converter.ConvertFrom("#7289DA")!;
            btnSend.BorderBrush = (SolidColorBrush)converter.ConvertFrom("#7289DA")!;
            btnConnect.Background = (SolidColorBrush)converter.ConvertFrom("#7289DA")!;
            btnConnect.BorderBrush = (SolidColorBrush)converter.ConvertFrom("#7289DA")!;
            btnDisconnect.Background = (SolidColorBrush)converter.ConvertFrom("#FF5733")!;
            btnDisconnect.BorderBrush = (SolidColorBrush)converter.ConvertFrom("#FF5733")!;
        }
    }
}