using System.Net.NetworkInformation;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System;
using Microsoft.Win32;
using System.IO;
using System.Net.Sockets;
using System.Web;
using System.Net;
using System.Text;
using System.Configuration;

namespace MarineBoxFinding
{
    public class PingNetwork
    {
        public static List<Ping> pingers = new List<Ping>();
        public static int instances = 0;
        public static int timeOut = 250;
        public static int ttl = 5;

        public static void CreatePingers(int cnt)
        {
            for (int i = 1; i <= cnt; i++)
            {
                Ping p = new Ping();
                pingers.Add(p);
            }
        }

        public static void DestroyPingers()
        {
            foreach (Ping p in pingers)
            {
                p.Dispose();
            }
            pingers.Clear();
        }
    }

    class Program
    {
        public static void Main()
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            int myPort = Convert.ToInt32(config.AppSettings.Settings["myPort"].Value.ToString());
            string startingPage = config.AppSettings.Settings["startingPage"].Value;
            string fn = config.AppSettings.Settings["totalCheck"].Value;
            string serverRQA = config.AppSettings.Settings["serverRQA"].Value;
            List<string> activeIP = new List<string>();
            String log = "1) Scanning Network ...";
            Console.WriteLine(log);
            File.Delete(serverRQA);
            File.Delete(fn);

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                Console.WriteLine(ni.Name);
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                log += "MAC: " + ni.GetPhysicalAddress() + Environment.NewLine;
                log += "Gateways:" + Environment.NewLine;

                Console.WriteLine("Operational? {0}", ni.OperationalStatus == OperationalStatus.Up);
                Console.WriteLine("MAC: {0}", ni.GetPhysicalAddress());
                Console.WriteLine("Gateways:");
                foreach (GatewayIPAddressInformation gipi in ni.GetIPProperties().GatewayAddresses)
                {
                    log += gipi.Address + Environment.NewLine;
                    Console.WriteLine("\t{0}", gipi.Address);
                }
                log += "IP Addresses: ";
                Console.WriteLine("IP Addresses:");
                String IPv4 = "";
                foreach (UnicastIPAddressInformation uipi in ni.GetIPProperties().UnicastAddresses)
                {
                    if (uipi.Address.AddressFamily.ToString() != "InterNetworkV6")
                    {
                        if (uipi.Address.ToString() == "127.0.0.1") continue;
                        IPv4 = uipi.Address.ToString();
                        log += uipi.Address.ToString() + " Subnet Mask: " + uipi.IPv4Mask.ToString() + Environment.NewLine;
                        Console.WriteLine("\t{0} / {1}", uipi.Address, uipi.IPv4Mask);
                    }
                }
                if (IPv4 == "" || IPv4 == "127.0.0.1") continue;

                //ping entire network of NICs
                string baseIP = IPv4.Substring(0, IPv4.LastIndexOf(".") + 1);

                Console.WriteLine("Pinging 255 destinations of D-class in {0}*", baseIP);

                //number of IP in subnet
                PingNetwork.CreatePingers(255);

                PingOptions po = new PingOptions(PingNetwork.ttl, true);
                System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
                byte[] data = enc.GetBytes("abababababababababababababababab");

                int cnt = 1;
                int index = 1;

                // hthngoc - remove async running due to missing Active IP - to check RQA server.
                // must run slowly in sequence.
                foreach (Ping p in PingNetwork.pingers)
                {
                    PingReply reply= p.Send(string.Concat(baseIP, cnt.ToString()), PingNetwork.timeOut);
                    if (reply.Status == IPStatus.Success)
                    {
                        Console.WriteLine(string.Concat("Active IP: ", reply.Address.ToString()));
                        activeIP.Add(reply.Address.ToString());
                        FindRQAServer(index, reply.Address.ToString(), myPort, serverRQA);
                        index += 1;
                        log += Environment.NewLine + reply.Address.ToString();
                    }
                    cnt += 1;
                }
            }            

            Console.WriteLine("\n2)Updating the Starting point...\n");
            string updatedResult = UpdateStartingPage(startingPage, serverRQA);

            log += updatedResult;
            File.AppendAllText(fn, log);

            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.WriteLine("Done, please Press Any Key to quit. \nPlease get the file in \"" + fn + "\" for Wallem support.");
            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.WriteLine("=========");
            Console.ReadKey();
        }

        /// <summary>
        /// hthngoc - Separate with Scan IP func due to 
        ///     + its async process.        
        ///     + skip invalid pointed.
        /// </summary>
        /// <param name="ipList"></param>
        public static void FindRQAServer(int i, string ip, int myPort, string serverRQAFile)
        {
            String toWrite = "";
                string url = string.Format("http://{0}:{1}/", ip, myPort);
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "GET";
                    request.KeepAlive = true;
                    request.UserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR 1.1.4322)";
                    request.ContentType = "text/xml";
                    request.Credentials = System.Net.CredentialCache.DefaultNetworkCredentials;
                    request.Timeout = 1000;
                    // execute the request
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.Headers["set-cookie"].Contains("RQA"))
                        {
                            Console.WriteLine(string.Format("{0}. Found RQA Server at: {1}", i.ToString(), url));
                            toWrite += ip + ";";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("{0}. Skipped {1}", i.ToString(), url));
                }
                File.AppendAllText(serverRQAFile, toWrite);
        }
        /// <summary>
        /// hthngoc - update IP in source Starting point.
        /// Use the smallest IP if have many RQA servers.
        /// </summary>
        /// <param name="startingPage"></param>
        /// <param name="serverRQA"></param>
        public static string UpdateStartingPage(string startingPage, string serverRQA)
        {
            string rs =  Environment.NewLine + "2) List of RQA servers:" + Environment.NewLine;
            Console.WriteLine(rs);
            if (File.Exists(startingPage))
            {
                string html = File.ReadAllText(startingPage);
                string servers = File.ReadAllText(serverRQA);
                string[] serverList = servers.Split(';');
                IPAddress[] list = new IPAddress[serverList.Length-1];
                
                for (int i = 0; i < serverList.Length-1; i++)
                {
                    if (serverList[i] != string.Empty)
                    {
                        list[i] = IPAddress.Parse(serverList[i]);
                    }
                }

                List<IPAddress> listsorted = new List<IPAddress>(list);
                listsorted.Sort(new IPAddressComparer());
                for (int i = 0; i < listsorted.Count; i++)
                {
                    Console.WriteLine(listsorted[i].ToString());
                    rs += listsorted[i].ToString() + Environment.NewLine;
                }

                html = html.Replace(" var IPaddress =\"",
                    string.Format(" var IPaddress =\"{0}\";//", listsorted[0].ToString()));
                rs += "Used RQA server for starting point: " + listsorted[0].ToString();
                Console.WriteLine("Used RQA server for starting point: " + listsorted[0].ToString());                
                File.WriteAllText(startingPage, html);
            }
            else
            {
                Console.WriteLine("Cannot found Starting point on this machine");
                Console.WriteLine("File does not exist: " + startingPage);
                rs += "Cannot found Starting point on this machine, File does not exist: " + startingPage;
            }
            return rs;
        }
    }
    /// <summary>
    /// hthngoc - Sorting IP address
    /// </summary>
    public class IPAddressComparer : IComparer<IPAddress>
    {
        public int Compare(IPAddress x, IPAddress y)
        {
            int retVal = 0;
            byte[] bytesX = x.GetAddressBytes();
            byte[] bytesY = y.GetAddressBytes();

            for (int i = 0; (i < bytesX.Length) && (i < bytesY.Length); i++)
            {
                retVal = bytesX[i].CompareTo(bytesY[i]);
                if (0 != retVal)
                {
                    break;
                }
            }
            return retVal;
        }
    }
    
}
