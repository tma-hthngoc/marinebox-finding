using System;
using System.Threading;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.IO;
using System.Configuration;
using System.Net;

public class MonitorSample
{

    public static string IPNetwork = "1) Scanning Network ...";
    public static string baseIP = "";
    public static List<string> activeIP = new List<string>();
    static Configuration config = ConfigurationManager.OpenExeConfiguration(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
    static int myPort;
    static string startingPage;
    static string fn;
    static string serverRQA;
    public static void Main(String[] args)
    {
        int result = 0;   // Result initialized to say there is no error
         myPort = Convert.ToInt32(config.AppSettings.Settings["myPort"].Value.ToString());
         startingPage = config.AppSettings.Settings["startingPage"].Value;
         fn = config.AppSettings.Settings["totalCheck"].Value;
         serverRQA = config.AppSettings.Settings["serverRQA"].Value;
         Console.WriteLine(IPNetwork);
        File.Delete(serverRQA);
        File.Delete(fn);

        // Threads producer and consumer have been created, 
        // but not started at this point.
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            Console.WriteLine();
            Console.WriteLine(ni.Name);
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            String IPv4 = "";
            Console.WriteLine("Operational? {0}", ni.OperationalStatus == OperationalStatus.Up);
            Console.WriteLine("MAC: {0}", ni.GetPhysicalAddress());
            Console.WriteLine("Gateways:");
            foreach (GatewayIPAddressInformation gipi in ni.GetIPProperties().GatewayAddresses)
            {
                IPNetwork += gipi.Address + Environment.NewLine;
                Console.WriteLine("\t{0}", gipi.Address);
            }
            IPNetwork += Environment.NewLine + "IP Addresses: ";
            Console.WriteLine("IP Addresses:");
            foreach (UnicastIPAddressInformation uipi in ni.GetIPProperties().UnicastAddresses)
            {
                if (uipi.Address.AddressFamily.ToString() != "InterNetworkV6")
                {
                    if (uipi.Address.ToString() == "127.0.0.1") continue;
                    IPv4 = uipi.Address.ToString();
                    IPNetwork += uipi.Address.ToString() + " Subnet Mask: " + uipi.IPv4Mask.ToString() + Environment.NewLine;
                    Console.WriteLine("\t{0} / {1}", uipi.Address, uipi.IPv4Mask);
                }
            }

            if (IPv4 == "" || IPv4 == "127.0.0.1") continue;

            //ping entire network of NICs

            baseIP = IPv4.Substring(0, IPv4.LastIndexOf(".") + 1);

            Thread producer = new Thread(new ThreadStart(MonitorSample.TryPinging));
            Thread consumer = new Thread(new ThreadStart(MonitorSample.TryPinging2));
            Thread thread3 = new Thread(new ThreadStart(MonitorSample.TryPinging3));
            Console.WriteLine();
            try
            {
                producer.Start();
                consumer.Start();
                thread3.Start();
                producer.Join();   // Join both threads with no timeout
                // Run both until done.
                consumer.Join();
                thread3.Join();
                // threads producer and consumer have finished at this point.
            }
            catch (ThreadStateException e)
            {
                Console.WriteLine(e);  // Display text of exception
                result = 1;            // Result says there was an error
            }
            catch (ThreadInterruptedException e)
            {
                Console.WriteLine(e);  // This exception means that the thread
                // was interrupted during a Wait
                result = 1;            // Result says there was an error
            }
            Console.WriteLine("Found " + activeIP.Count.ToString());

        }
        // Even though Main returns void, this provides a return code to 
        // the parent process.
        Environment.ExitCode = result;

        Console.WriteLine("\n2)Updating the Starting point...\n");
        string updatedResult = UpdateStartingPage(startingPage, serverRQA);

        IPNetwork += updatedResult;
        File.AppendAllText(fn, IPNetwork);

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("==================     FINISH    ===========================");   
        Console.WriteLine("Please send the file in \"" + fn + "\" for Wallem support.");
        Console.WriteLine("Please Press Any Key to quit.");
        Console.ReadLine();
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
                    object sync = new Object();
                    lock (sync)
                    {
                        Console.WriteLine(string.Format("Found RQA Server at: {0}", url));
                        toWrite += ip + ";";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(string.Format("Skipped {0}", url));
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
        string rs = Environment.NewLine + "List of RQA servers:" + Environment.NewLine;
        Console.WriteLine(rs);
        if (File.Exists(startingPage))
        {
            string html = File.ReadAllText(startingPage);
            string servers = File.ReadAllText(serverRQA);
            string[] serverList = servers.Split(';');
            IPAddress[] list = new IPAddress[serverList.Length - 1];

            for (int i = 0; i < serverList.Length - 1; i++)
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


    public static void TryPinging()
    {
        for (int i = 1; i < 75; i++)
        {
            string ip = baseIP + i.ToString();
            TryPing0(i, ip);
        }
    }
    public static void TryPinging2()
    {
        for (int i = 76; i < 154; i++)
        {
            string ip = baseIP + i.ToString();
            TryPing0(i, ip);
        }
    }
    public static void TryPinging3()
    {
        for (int i = 155; i < 256; i++)
        {
            string ip = baseIP + i.ToString();
            TryPing0(i, ip);
        }
    }
    public static void TryPing0(int index, string destinationIP)
    {
        Ping p = new Ping();
        PingReply reply = p.Send(destinationIP, 250);
        if (reply.Status == IPStatus.Success)
        {
            //Console.WriteLine(index.ToString() + string.Concat(". Active IP: ", reply.Address.ToString()));
            
                activeIP.Add(reply.Address.ToString());
                FindRQAServer(index, reply.Address.ToString(), myPort, serverRQA);
                IPNetwork += Environment.NewLine + reply.Address.ToString();
            
        }
        else
        {
            IPNetwork += Environment.NewLine + "Skipped: " + destinationIP;
        }
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

    

