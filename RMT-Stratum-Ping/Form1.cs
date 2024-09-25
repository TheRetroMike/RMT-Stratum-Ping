using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices.WindowsRuntime;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Diagnostics;

namespace RMT_Stratum_Ping
{
    public partial class Form1 : Form
    {
        bool ipv6 = false;
        int count = 5;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            stratumTextBox.Text = "stratum+tcp://us.kaspa.herominers.com:1208";
        }

        private void resetButton_Click(object sender, EventArgs e)
        {
            stratumTextBox.Text = String.Empty;
            resultsTextBox.Text = String.Empty;
        }

        private void executeButton_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(stratumTextBox.Text))
            {
                MessageBox.Show("At least one stratum endpoint must be specified");
                return;
            }

            Process();
        }

        async Task Process()
        {
            var lines = stratumTextBox.Text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                foreach (var line in lines)
                {
                    await Run(line);
                }
            }
        }

        async Task Run(string poolStratum)
        {
            try
            {
                var proto = "stratum2";
                var uri = new Uri(poolStratum);

                var scheme = uri.Scheme;
                var host = uri.Host;
                var port = uri.Port;
                var isTls = false;

                var addr = await ResolveAsync(host);
                switch (scheme)
                {
                    case "stratum+tls":
                        isTls = true;
                        break;
                    case "stratum+eth":
                        proto = "stratum1";
                        break;
                    default:
                        break;
                }

                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("PING stratum {0} ({1}) {3} port {2}", host, addr, port, isTls ? "TLS" : String.Empty);

                TimeSpan min = TimeSpan.FromHours(1);
                TimeSpan max = TimeSpan.Zero;
                TimeSpan avg = TimeSpan.Zero;
                int avgCount = 0;
                int success = 0;
                DateTime start = DateTime.Now;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        TimeSpan elapsed = await DoPingAsync(addr, isTls, host, port, proto);
                        resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("{0} ({1}): seq={2}, time={3}", host, addr, i, elapsed.Milliseconds);
                        if (elapsed > max)
                            max = elapsed;
                        if (elapsed < min)
                            min = elapsed;
                        avg += elapsed;
                        avgCount++;
                        success++;
                    }
                    catch (Exception ex)
                    {
                        resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("{0} ({1}): seq={2}, {3}", host, addr, i, ex.Message);
                    }
                    await Task.Delay(1000);
                }

                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("\r\n--- {0} ping statistics ---", host);
                int loss = 100 - (int)(((double)success / count) * 100.0);
                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("{0} packets transmitted, {1} received, {2}% packet loss, time {3}", count, success, loss, DateTime.Now - start);
                if (success > 0)
                {
                    resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("min/avg/max = {0} ms, {1} ms, {2} ms", min.Milliseconds, TimeSpan.FromTicks(avg.Ticks / avgCount).Milliseconds, max.Milliseconds);
                }
                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("\r\n--------------\r\n");
            }
            catch(Exception ex)
            {
                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("\r\n--------------\r\n");
                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + ex.Message + " - " + poolStratum;
                resultsTextBox.Text = resultsTextBox.Text + "\r\n" + String.Format("\r\n--------------\r\n");
            }
        }

        private async Task<IPAddress> ResolveAsync(string host)
        {
            AddressFamily family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            IPHostEntry hostEntry = await Dns.GetHostEntryAsync(host);
            var addr = Array.Find(hostEntry.AddressList, a => a.AddressFamily == family);
            if (addr == null)
            {
                throw new Exception($"Failed to resolve host name: {host}");
            }
            return addr;
        }

        private async Task<TimeSpan> DoPingAsync(IPAddress addr, bool tls, string host, int port, string proto)
        {
            string dial = ipv6 ? $"[{addr}]:{port}" : $"{addr}:{port}";
            using (TcpClient client = new TcpClient(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork))
            {

                DateTime start = DateTime.Now;
                await client.ConnectAsync(addr, port);

                Stream stream = client.GetStream();
                if (tls)
                {
                    SslStream sslStream = new SslStream(stream, false, (sender, certificate, chain, errors) => true);
                    await sslStream.AuthenticateAsClientAsync(host);
                    stream = sslStream;
                }

                using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII, 1024, true))
                {
                    var requestData = "";
                    switch (proto)
                    {
                        case "stratum1":
                            requestData = "{\"id\":1,\"jsonrpc\":\"2.0\",\"method\":\"eth_submitLogin\",\"params\":[\"login\",\"pass\"]";
                            break;
                        case "stratum2":
                            requestData = "{\"id\":1,\"jsonrpc\":\"2.0\",\"method\":\"mining.subscribe\",\"params\":[\"stratum-ping/1.0.0\",\"EthereumStratum/1.0.0\"]";
                            break;
                        default:
                            break;
                    }
                    writer.Write(requestData);
                }
                return DateTime.Now - start;

            }
        }
    }
}
