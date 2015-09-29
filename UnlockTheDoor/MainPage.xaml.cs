/*
 Based on Blinky Demo
    Copyright(c) Microsoft Open Technologies, Inc. All rights reserved.

    The MIT License(MIT)

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files(the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions :

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace UnlockTheDoor
{
    public sealed partial class MainPage : Page
    {
        Task _receiveTask = null;
        public MainPage()
        {
            InitializeComponent();
            IsPiInitialised = false;

            Unloaded += MainPage_Unloaded;

            InitGPIO();

//            _receiveTask = GetMessages(); //moved to timer_tick

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void InitGPIO()
        {
            var gpio = GpioController.GetDefault();

            // Show an error if there is no GPIO controller
            if (gpio == null)
            {
                relay = null;
                GpioStatus.Text = "There is no GPIO controller on this device.";
                return;
            }
            pushButton = gpio.OpenPin(PB_PIN);
            relay = gpio.OpenPin(Relay_PIN);

            // Show an error if the pin wasn't initialized properly
            if (relay == null)
            {
                GpioStatus.Text = "There were problems initializing the GPIO Relay pin.";
                return;
            }
            if (pushButton == null)
            {
                GpioStatus.Text = "There were problems initializing the GPIO Push Button pin.";
                return;
            }

            pushButton.SetDriveMode(GpioPinDriveMode.Input);
            relay.SetDriveMode(GpioPinDriveMode.Output);

            GpioStatus.Text = "GPIO pin initialized correctly.";
        }

        private void MainPage_Unloaded(object sender, object args)
        {
            // Cleanup
            relay.Dispose();
            pushButton.Dispose();
            if (cancellationToken != null)
            {
                cancellationToken.Cancel();
                cancellationToken.Dispose();
                cancellationToken = null;
            }
        }

        private void SwitchRelay(bool turnOn)
        {
            if (turnOn)
            {
                if (RelayStatus == 0)
                {
                    RelayStatus = 1;
                    if (relay != null)
                    {
                        // to turn on the relay, we need to push the pin 'low'
                        relay.Write(GpioPinValue.Low);
                    }
                    Relay.Fill = redBrush;
                }
            }
            else
            {
                RelayStatus = 0;
                if (relay != null)
                {
                    relay.Write(GpioPinValue.High);
                }
                Relay.Fill = grayBrush;
            }
        }

        private void TurnOffRelay()
        {
            if (RelayStatus == 1)
            {
                SwitchRelay(true);
            }
        }

        private async void Timer_Tick(object sender, object e)
        {
            try
            {
                HandleInitialisation();

                var pushButtonValue = pushButton.Read();
                HandleButton(pushButtonValue == GpioPinValue.Low);

                HandleManualUnlock();
                if (_receiveTask == null || !(_receiveTask.Status == TaskStatus.Running || _receiveTask.Status == TaskStatus.WaitingToRun || _receiveTask.Status == TaskStatus.WaitingForActivation))
                {
                    // the receive task is not running
                    _receiveTask = GetMessages();
                }
            }
            catch (Exception ex)
            {
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                                    () =>
                                    {
                                        GpioStatus.Text = ex.Message;
                                    });
            }
        }

        private async void HandleInitialisation()
        {
            if(!IsPiInitialised)
            {
                // send a message but its likely to fail until the network is working
                string token = GetSASToken(baseAddress, user, key);

                Dictionary<string, string> properties = new Dictionary<string, string>();

                properties.Add("Device", "RaspberryPi");
                var response = await SendMessage("https://<yournamespace>.servicebus.windows.net/", topic, token, "PiInitialised", properties);
                if(response != null && response.IsSuccessStatusCode )
                {
                    // stop trying to send a message on the service bus
                    IsPiInitialised = true;
                }

            }
            else if (IsSBInitialised)
            {
                initialisedCount++;
                if (initialisedCount > 6)
                {
                    if (initialisedCount > 10)
                    {
                        if (initialisedCount > 14)
                        {
                            // turn off and reset
                            IsSBInitialised = false;
                            initialisedCount = 0;
                            SwitchRelay(false);
                        }
                        else
                        {
                            // turn on
                            SwitchRelay(true);
                        }

                    }
                    else
                    {
                        // turn off
                        SwitchRelay(false);
                    }
                }
            }
        }

        private void HandleManualUnlock()
        {
            if (isManualUnlock)
            {
                manualUnlockCount++;
                if (manualUnlockCount > 20)
                {
                    manualUnlockCount = 0;
                    isManualUnlock = false;
                    SwitchRelay(false);
                }
            }
        }

        bool WasPressed = false;
        int PressedCount = 0;
        private async void HandleButton(bool IsPressed)
        {
            if (IsPressed && !WasPressed)
            {
                // Relay is now on
                //SwitchRelay(false);
                try
                {
                    string token = GetSASToken(baseAddress, user, key);

                    Dictionary<string, string> properties = new Dictionary<string, string>();

                    properties.Add("Priority", "High");
                    properties.Add("MessageType", "Command");
                    properties.Add("Command", "BingBong");
                    SendMessage("https://<yournamespace>.servicebus.windows.net/", topic, token, "BingBong", properties);
                }
                catch (Exception ex)
                {
                    var exc = ex.Message;

                }
            }
            else if (!IsPressed && WasPressed)
            {
                //changed state to NOT pressed
                if(PressedCount >= 12)
                {
                    SwitchRelay(false);
                }
                PressedCount = 0;
            }
            else if(IsPressed && WasPressed)
            {
                PressedCount++;
                if(PressedCount == 12)
                {
                    // we've held it dow for around 3 seconds so unlock
                    SwitchRelay(true);
                }
            }

            WasPressed = IsPressed;
        }

        CancellationTokenSource cancellationToken = new CancellationTokenSource();
        public async Task GetMessages()
        {
            CancellationToken ct = cancellationToken.Token;
            await Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            // Clean up here, then...
                            ct.ThrowIfCancellationRequested();
                        }

                        string token = GetSASToken("https://<yournamespace>.servicebus.windows.net/", user, key);

                        Dictionary<string, string> properties = new Dictionary<string, string>();

                        properties.Add("Priority", "High");
                        properties.Add("MessageType", "Command");
                        string message = await ReceiveAndDeleteMessageFromSubscription("https://<yournamespace>.servicebus.windows.net/", topic, subscription, token, null);

                        if (message.Contains("Unlock"))
                        {
                            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                                    () =>
                                    {
                                        SwitchRelay(true);
                                    });
                        }
                        else if (message.Contains("Lock"))
                        {
                            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                                    () =>
                                    {
                                        SwitchRelay(false);
                                    });
                        }
                        else if (message.Contains("PiInitialised"))
                        {
                            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                                    () =>
                                    {
                                        IsSBInitialised = true;
                                        SwitchRelay(true);
                                    });
                        }

                    }
                }
                catch (Exception ex)
                {
                    var exc = ex.Message;

                }
            }, ct);
        }

        private async System.Threading.Tasks.Task<HttpResponseMessage> SendMessage(string baseAddress, string queueTopicName, string token, string body, IDictionary<string, string> properties)
        {
            string fullAddress = baseAddress + queueTopicName + "/messages" + "?timeout=60&api-version=2013-08 ";
            return await SendViaHttp(token, body, properties, fullAddress, HttpMethod.Post);
        }

        private static async System.Threading.Tasks.Task<HttpResponseMessage> SendViaHttp(string token, string body, IDictionary<string, string> properties, string fullAddress, HttpMethod httpMethod)
        {
            HttpClient webClient = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage()
            {
                RequestUri = new Uri(fullAddress),
                Method = httpMethod,

            };
            webClient.DefaultRequestHeaders.Add("Authorization", token);

            if (properties != null)
            {
                foreach (string property in properties.Keys)
                {
                    request.Headers.Add(property, properties[property]);
                }
            }
            request.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("", body) });
            HttpResponseMessage response = null;
            try
            {
                response = await webClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string error = string.Format("{0} : {1}", response.StatusCode, response.ReasonPhrase);
                    //throw new Exception(error);
                }
            }
            catch(Exception ex)
            {
                
            }
            return response;
        }


        // Receives and deletes the next message from the given resource (queue, topic, or subscription)
        // using the resourceName and an HTTP DELETE request.
        private static async System.Threading.Tasks.Task<string> ReceiveAndDeleteMessageFromSubscription(string baseAddress, string topic, string subscription, string token, IDictionary<string, string> properties)
        {
            string fullAddress = baseAddress + topic + "/Subscriptions/" + subscription + "/messages/head" + "?timeout=60";
            HttpResponseMessage response = await SendViaHttp(token, "", properties, fullAddress, HttpMethod.Delete);
            string content = "";
            if (response.IsSuccessStatusCode)
            {
                // we should have retreived a message
                content = await response.Content.ReadAsStringAsync();
            }
            return content;
        }


        private IDictionary<string, string> GetConnectionStringParts(string connectionString)
        {
             string[] connectionStringParts = connectionString.Split(';');
            string baseaddress = connectionStringParts[0].Replace("Endpoint=", "");
            baseaddress = baseaddress.Replace("sb://", "https://");
            string sharedAccessKeyName = connectionStringParts[1].Replace("SharedAccessKeyName=", "");
            string sharedAccessKey = connectionStringParts[2].Replace("SharedAccessKey=", "");

            IDictionary<string, string> parts = new System.Collections.Generic.Dictionary<string, string>();
            parts.Add("baseaddress", baseaddress);
            parts.Add("sharedAccessKeyName", sharedAccessKeyName);
            parts.Add("sharedAccessKey", sharedAccessKey);

            return parts;
        }

        private string GetSASToken(string baseAddress, string SASKeyName, string SASKeyValue)
        {
            TimeSpan fromEpochStart = DateTime.UtcNow - new DateTime(1970, 1, 1);
            string expiry = Convert.ToString((int)fromEpochStart.TotalSeconds + 3600);
            string stringToSign = WebUtility.UrlEncode(baseAddress) + "\n" + expiry;
            string hmac = GetSHA256Key(Encoding.UTF8.GetBytes(SASKeyValue), stringToSign);
            string hash = HmacSha256(SASKeyValue, stringToSign);
            string sasToken = String.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
                WebUtility.UrlEncode(baseAddress), WebUtility.UrlEncode(hash), expiry, SASKeyName);
            return sasToken;
        }
        public string HmacSha256(string secretKey, string value)
        {
            // Move strings to buffers.
            var key = CryptographicBuffer.ConvertStringToBinary(secretKey, BinaryStringEncoding.Utf8);
            var msg = CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8);

            // Create HMAC.
            var objMacProv = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256);
            var hash = objMacProv.CreateHash(key);
            hash.Append(msg);
            return CryptographicBuffer.EncodeToBase64String(hash.GetValueAndReset());
        }

        private string GetSHA256Key(byte[] secretKey, string value)
        {
            var objMacProv = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256);
            var hash = objMacProv.CreateHash(secretKey.AsBuffer());
            hash.Append(CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8));
            return CryptographicBuffer.EncodeToBase64String(hash.GetValueAndReset());
        }
        private int RelayStatus = 0;
        private const int PB_PIN = 27;
        private const int Relay_PIN = 5;
        private GpioPin relay;
        private GpioPin pushButton;
        private DispatcherTimer timer;
        private SolidColorBrush redBrush = new SolidColorBrush(Windows.UI.Colors.Red);
        private SolidColorBrush grayBrush = new SolidColorBrush(Windows.UI.Colors.LightGray);
        #region constants
        private const string ConnectionString = "Endpoint = sb://<yournamespace>.servicebus.windows.net/;SharedAccessKeyName=<YourUserFromAzureServiceBusPortal>;SharedAccessKey=<YourKeyFromAzureServiceBusPortal>";
        private const string topic = "testtopic";
        private const string subscription = "RaspberryPi2";
        private const string baseAddress = "https://<yournamespace>.servicebus.windows.net/";
        private const string user = "<YourUserFromAzureServiceBusPortal>";
        private const string key = "<YourKeyFromAzureServiceBusPortal>";
        private bool isManualUnlock = false;
        private int manualUnlockCount = 0;

        public bool IsSBInitialised { get; private set; }
        public int initialisedCount { get; private set; }
        public bool IsPiInitialised { get; private set; }

        #endregion

        private void button_Click(object sender, RoutedEventArgs e)
        {
            SwitchRelay(true);
            isManualUnlock = true;
        }
    }
    //class TopicSub { public string Topic; public string Sub; }

}
