// <copyright file="OPCDAHelper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

// https://github.com/titanium-as/TitaniumAS.Opc.Client
// https://github.com/chkr1011/MQTTnet
namespace Dgiot_dtu
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Da;
    using MQTTnet.Core;
    using MQTTnet.Core.Client;
    using MQTTnet.Core.Protocol;
    using TitaniumAS.Opc.Client.Common;
    using TitaniumAS.Opc.Client.Da;
    using TitaniumAS.Opc.Client.Da.Browsing;

    public class OPCDAHelper
    {
        private const bool V = false;
        private static string topic = "thing/opcda/";
        private static string opcserver = "127.0.0.1";
        private static List<string> serverlist = new List<string> { };
        private static MainForm mainform = null;
        private static OPCDAHelper instance = null;
        private static string clientid = string.Empty;
        private static bool bIsRun = V;
        private static bool bIsCheck = false;
        private static SocketService socketserver = new SocketService();
        private static List<TreeNode> dataList = null;

        public static OPCDAHelper GetInstance()
        {
            if (instance == null)
            {
                instance = new OPCDAHelper();
            }

         return instance;
        }

        public static void Start(KeyValueConfigurationCollection config, MainForm mainform)
        {
            Config(config, mainform);
            socketserver.Start();
            bIsRun = true;
        }

        public static void Stop()
        {
            socketserver.Stop();
            bIsRun = false;
        }

        public static void Config(KeyValueConfigurationCollection config, MainForm mainform)
        {
            if (config["OPCDAIsCheck"] != null)
            {
                bIsCheck = StringHelper.StrTobool(config["OPCDAIsCheck"].Value);
            }

            if (config["OPCDATopic"] != null)
            {
                topic = config["OPCDATopic"].Value;
            }

            if (config["OpcServer"] != null)
            {
                opcserver = config["OpcServer"].Value;
            }

            OPCDAHelper.mainform = mainform;
        }

        public static void Do_opc_da(MqttClient mqttClient, string topic, Dictionary<string, object> json, string clientid, MainForm mainform)
        {
            Regex r_subopcda = new Regex(OPCDAHelper.topic); // ����һ��Regex����ʵ��
            Match m_subopcda = r_subopcda.Match(topic); // ���ַ�����ƥ��

            if (!m_subopcda.Success)
            {
                return;
            }

            mainform.Log(topic);

            OPCDAHelper.mainform = mainform;
            OPCDAHelper.clientid = clientid;
            string cmdType = "read";
            if (json.ContainsKey("cmdtype"))
            {
                try
                {
                    cmdType = (string)json["cmdtype"];
                    switch (cmdType)
                    {
                        case "scan":
                            Scan_opc_da(json);
                            break;
                        case "read":
                            Read_opc_da(json);
                            break;
                        case "write":
                            break;
                        default:
                            Read_opc_da(json);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    mainform.Log(ex.ToString());
                }
            }
        }

        public static List<string> GetServer()
        {
            List<string> addresses = new List<string> { opcserver };
            dataList = socketserver.ScanOPCClassicServer(addresses);
            serverlist.Clear();
            foreach (TreeNode node in dataList)
            {
                if (node.Children.Any())
                {
                    foreach (TreeNode childnode in node.Children)
                    {
                        serverlist.Add(childnode.Name);
                        Recursion(childnode);
                    }
                }
            }

            return serverlist;
        }

        private static void Recursion(TreeNode childNode)
        {
            if (childNode.Children.Any())
            {
                var children = childNode.Children.ToList();
                children.ForEach((child) =>
                {
                    Recursion(child);
                });
            }
        }

        private static void Scan_opc_da(Dictionary<string, object> json)
        {
            // string opcserver = "Matrikon.OPC.Simulation.1";
            string opcserver = "Kepware.KEPServerEX.V6";

            IList<OpcDaItemDefinition> itemlist = new List<OpcDaItemDefinition>();
            if (json.ContainsKey("opcserver"))
            {
                try
                {
                    opcserver = (string)json["opcserver"];
                }
                catch (Exception ex)
                {
                    mainform.Log(ex.ToString());
                }
            }

            Uri url = UrlBuilder.Build(opcserver);
            try
            {
                using (var server = new OpcDaServer(url))
                {
                    // Connect to the server first.
                    server.Connect();
                    var browser = new OpcDaBrowserAuto(server);
                    JsonObject scan = new JsonObject();
                    BrowseChildren(scan, browser);
                    MqttClientHelper.Publish(topic + "/metadata/derived", Encoding.UTF8.GetBytes(scan.ToString()));
                }
            }
            catch (Exception ex)
            {
                mainform.Log("error  " + ex.GetBaseException().ToString());
                JsonObject result = new JsonObject();
                result.Add("TimeStamp", FromDateTime(DateTime.UtcNow));
                result.Add("opcserver", opcserver);
                result.Add("status", ex.GetHashCode());
                result.Add("err", ex.ToString());
                MqttClientHelper.Publish(topic + "/event/scanfailed", Encoding.UTF8.GetBytes(result.ToString()));
            }
        }

        private static void BrowseChildren(JsonObject json, IOpcDaBrowser browser, string itemId = null, int indent = 0)
        {
            // When itemId is null, root elements will be browsed.
            OpcDaBrowseElement[] elements = browser.GetElements(itemId);
            JsonArray array = new JsonArray();
            bool flag = false;
            foreach (OpcDaBrowseElement element in elements)
            {
                // Skip elements without children.
               if (!element.HasChildren)
                {
                    array.Add(element);
                    flag = true;
                    continue;
                }

                // Output children of the element.
                BrowseChildren(json, browser, element.ItemId, indent + 2);
            }

            if (flag)
            {
                if (itemId != null)
                {
                    json.Add(itemId, array);
                }
            }
        }

        private static void Read_opc_da(Dictionary<string, object> json)
        {
            string opcserver = "Matrikon.OPC.Simulation.1";
            string group = "addr";
            IList<OpcDaItemDefinition> itemlist = new List<OpcDaItemDefinition>();
            if (json.ContainsKey("opcserver"))
            {
                try
                {
                    opcserver = (string)json["opcserver"];
                }
                catch (Exception ex)
                {
                    mainform.Log(ex.ToString());
                }
            }

            if (json.ContainsKey("group"))
            {
                try
                {
                    group = (string)json["group"];
                }
                catch (Exception ex)
                {
                    mainform.Log(ex.ToString());
                }
            }

            if (json.ContainsKey("items"))
            {
                try
                {
                    string items = (string)json["items"];
                    string[] arry = items.Split(',');
                    JsonObject data = new JsonObject();
                    try
                    {
                        JsonObject result = new JsonObject();
                        Read_group(opcserver, group, arry, data);
                        result.Add("status", 0);
                        result.Add(group, data);
                        mainform.Log("result " + result.ToString());
                        MqttClientHelper.Publish(topic + "/properties/read/reply", Encoding.UTF8.GetBytes(result.ToString()));
                    }
                    catch (Exception ex)
                    {
                        mainform.Log(ex.ToString());
                    }
                }
                catch (Exception ex)
                {
                    mainform.Log(ex.ToString());
                }
            }
        }

        private static void Read(string opcserver, string group_name, string[] arry, JsonObject items)
        {
            Uri url = UrlBuilder.Build(opcserver);
            try
            {
                using (var server = new OpcDaServer(url))
                {
                    // Connect to the server first.
                    foreach (string id in arry)
                    {
                        server.Connect();
                        OpcDaGroup group = server.AddGroup(group_name);
                        var definition = new OpcDaItemDefinition
                        {
                            ItemId = id,
                            IsActive = true
                        };
                        group.IsActive = true;
                        OpcDaItemDefinition[] definitions = { definition };
                        OpcDaItemResult[] results = group.AddItems(definitions);
                        OpcDaItemValue[] values = group.Read(group.Items, OpcDaDataSource.Device);
                        foreach (OpcDaItemValue item in values)
                        {
                            mainform.Log(topic + "/properties/read/reply" + " " + id.ToString() + " " + item.GetHashCode().ToString() + " " + item.Value.ToString() + " " + item.Timestamp.ToString());
                            items.Add(id, item.Value);
                        }

                        server.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                mainform.Log(ex.GetBaseException().ToString());
                JsonObject result = new JsonObject();
                result.Add("opcserver", opcserver);
                result.Add("status", ex.GetHashCode());
                result.Add("err", ex.ToString());
                MqttClientHelper.Publish(topic + "/event/readfailed", Encoding.UTF8.GetBytes(items.ToString()));
            }
        }

        private static void Read_group(string opcserver, string group_name, string[] arry, JsonObject items)
        {
            Uri url = UrlBuilder.Build(opcserver);
            try
            {
                using (var server = new OpcDaServer(url))
                {
                    // Connect to the server first.
                    server.Connect();

                    // Create a group with items.
                    OpcDaGroup group = server.AddGroup(group_name);
                    IList<OpcDaItemDefinition> definitions = new List<OpcDaItemDefinition>();
                    int i = 0;
                    foreach (string id in arry)
                    {
                        var definition = new OpcDaItemDefinition
                        {
                            ItemId = id,
                            IsActive = true
                        };
                        definitions.Insert(i++, definition);
                    }

                    group.IsActive = true;
                    OpcDaItemResult[] results = group.AddItems(definitions);
                    OpcDaItemValue[] values = group.Read(group.Items, OpcDaDataSource.Device);

                    // Handle adding results.
                    JsonObject data = new JsonObject();
                    foreach (OpcDaItemValue item in values)
                    {
                        mainform.Log(topic + "/properties/read/reply" + " " + item.GetHashCode().ToString() + " " + item.Value.ToString() + string.Empty + item.Timestamp.ToString());
                        data.Add(item.Item.ItemId, item.Value);
                    }

                    items.Add("status", 0);
                    items.Add(group_name, data);
                    mainform.Log(items.ToString());
                    MqttClientHelper.Publish(topic + "/properties/read/reply", Encoding.UTF8.GetBytes(items.ToString()));
                    server.Disconnect();
                }
            }
            catch (Exception ex)
            {
                mainform.Log(ex.ToString());
                Read(opcserver, group_name, arry, items);
            }
        }

        private static void Subscription_opc_da(string opcserver, string name)
        {
            Uri url = UrlBuilder.Build(opcserver);
            try
            {
                using (var server = new OpcDaServer(url))
                {
                    // Connect to the server first.
                    server.Connect();

                    // Create a group with items.
                    OpcDaGroup group = server.AddGroup("Group1");
                    group.IsActive = true;

                    var definition = new OpcDaItemDefinition
                    {
                        ItemId = name,
                        IsActive = true
                    };

                    OpcDaItemDefinition[] definitions = { definition };

                    OpcDaItemResult[] results = group.AddItems(definitions);

                    group.ValuesChanged += OnGroupValuesChanged;
                    group.UpdateRate = TimeSpan.FromMilliseconds(100);
                }
            }
            catch (Exception ex)
            {
                mainform.Log(ex.GetBaseException().ToString());
                JsonObject result = new JsonObject();
                result.Add("opcserver", opcserver);
                result.Add("name", name);
                result.Add("status", ex.GetHashCode());
                result.Add("err", ex.ToString());
                MqttClientHelper.Publish(topic + "/properties/read/reply", Encoding.UTF8.GetBytes(result.ToString()));
            }
        }

        private static void OnGroupValuesChanged(object sender, OpcDaItemValuesChangedEventArgs args)
        {
            // Output values.
            foreach (OpcDaItemValue value in args.Values)
            {
                mainform.Log("ItemId: " + value.Item.ItemId.ToString() + "; Value: {1}" + value.Value.ToString() +
                    ";Quality: " + value.Quality.ToString() + ";Timestamp: {3}" + value.Timestamp.ToString());
            }
        }

        private static DateTime baseTime = new DateTime(1970, 1, 1);

        /// <summary>
        /// ��unixtimeת��Ϊ.NET��DateTime
        /// </summary>
        /// <param name="timeStamp">����</param>
        /// <returns>ת�����ʱ��</returns>
        public static DateTime FromUnixTime(long timeStamp)
        {
            return TimeZone.CurrentTimeZone.ToLocalTime(new DateTime((timeStamp * 10000000) + baseTime.Ticks));
        }

        /// <summary>
        /// ��.NET��DateTimeת��Ϊunix time
        /// </summary>
        /// <param name="dateTime">��ת����ʱ��</param>
        /// <returns>ת�����unix time</returns>
        public static long FromDateTime(DateTime dateTime)
        {
            return (TimeZone.CurrentTimeZone.ToUniversalTime(dateTime).Ticks - baseTime.Ticks) / 10000000;
        }
    }
    }