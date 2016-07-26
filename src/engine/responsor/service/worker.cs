using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Win32;
using OpenETaxBill.Channel.Library.Security.Mime;
using OpenETaxBill.Channel.Library.Security.Notice;

namespace OpenETaxBill.Engine.Responsor
{
    public class Worker : WebListener
    {
        //-------------------------------------------------------------------------------------------------------------------------
        // 
        //-------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// 
        /// </summary>
        public Worker()
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p_portNumber"></param>
        public Worker(int p_portNumber)
            : base(p_portNumber)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p_localAddress"></param>
        /// <param name="p_portNumber"></param>
        public Worker(string p_localAddress, int p_portNumber)
            : base(p_localAddress, p_portNumber)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="p_hostAddress"></param>
        /// <param name="p_portNumber"></param>
        /// <param name="p_webFolder"></param>
        public Worker(string p_hostAddress, int p_portNumber, string p_webFolder)
            : base(p_hostAddress, p_portNumber)
        {
            m_webFolder = p_webFolder;
        }

        //-------------------------------------------------------------------------------------------------------------------------
        // 
        //-------------------------------------------------------------------------------------------------------------------------
        private string m_webFolder = null;
        public string WebFolder
        {
            get
            {
                if (m_webFolder == null)
                    m_webFolder = UAppHelper.WebFolder;

                return m_webFolder;
            }
            set
            {
                m_webFolder = value;
            }
        }

        private string m_defaultPage = null;
        public string DefaultPage
        {
            get
            {
                if (m_defaultPage == null)
                    m_defaultPage = UAppHelper.DefaultPage;

                return m_defaultPage;
            }
        }

        private ResponseEngine m_engine = null;
        public ResponseEngine REngine
        {
            get
            {
                if (m_engine == null)
                    m_engine = new ResponseEngine();

                return m_engine;
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------
        // 
        //-------------------------------------------------------------------------------------------------------------------------
        private static Queue m_syncQueue = null;
        private static Queue SyncQueue
        {
            get
            {
                if (m_syncQueue == null)
                    m_syncQueue = Queue.Synchronized(new Queue());

                return m_syncQueue;
            }
        }

        private static Thread QueueThread = null;

        private void Parser()
        {
            while (true)
            {
                lock (SyncQueue.SyncRoot)
                {
                    if (SyncQueue.Count > 0)
                    {
                        object _dequeue = SyncQueue.Dequeue();
                        if (_dequeue != null)
                        {
                            MimeContent _receiveMime = (MimeContent)_dequeue;

                            var _xmldoc = new XmlDocument();
                            _xmldoc.LoadXml(_receiveMime.Parts[1].GetContentAsString());

                            // ť���� �޽����� �����Ͽ� DB ó�� ��
                            REngine.ResultDataProcess(_xmldoc, DateTime.Now);
                        }
                    }
                    else
                    {
                        Thread.Sleep(1000);
                    }
                }

                Thread.Sleep(100);
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------
        // 
        //-------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// ����û���� ���� ����(����)��꼭 ó������� ���� �޾Ƽ� ó�� �մϴ�.
        /// </summary>
        /// <param name="p_request">����û�� ������ �޽���</param>
        /// <param name="p_response">����û���� ȸ�� �� �޽���</param>
        public override void OnResponse(ref HttpRequestStruct p_request, ref HttpResponseStruct p_response)
        {
            if (p_request.URL.ToLower() == UAppHelper.AcceptedRequestUrl)
            {
                try
                {
                    MimeContent _receiveMime = (new MimeParser()).DeserializeMimeContent(p_request.Headers["Content-Type"].ToString(), p_request.BodyData);

                    var _xmldoc = new XmlDocument();
                    _xmldoc.LoadXml(_receiveMime.Parts[0].GetContentAsString());

                    MimeContent _returnMime = REngine.ResultRcvAck(_xmldoc);
                    {
                        p_response.BodyData = _returnMime.GetContentAsBytes();

                        p_response.SoapAction = Request.eTaxResultRecvAck;
                        p_response.ContentType = _returnMime.ContentType;
                        p_response.ContentLength = p_response.BodyData.Length;

                        p_response.Headers.Add("SOAPAction", p_response.SoapAction);
                        p_response.Headers.Add("Content-Type", p_response.ContentType);
                        p_response.Headers.Add("Content-Length", p_response.ContentLength.ToString());

                        p_response.Status = (int)ResponseState.OK;
                    }

                    lock (SyncQueue.SyncRoot)
                    {
                        // ó�� �� �޽����� ť�� �߰� ��
                        SyncQueue.Enqueue(_receiveMime);

                        if (QueueThread == null || (QueueThread != null && QueueThread.IsAlive == false))
                        {
                            QueueThread = new Thread(Parser)
                            {
                                IsBackground = true
                            };
                            QueueThread.Start();
                        }
                    }
                }
                catch (Exception ex)
                {
                    IResponsor.WriteDebug(ex);

                    p_response.Status = (int)ResponseState.BAD_REQUEST;

                    string _bodyString
                        = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">\n"
                        + "<HTML><HEAD>\n"
                        + "<META http-equiv=Content-Type content=\"text/html; charset=UTF-8\">\n"
                        + "</HEAD>\n"
                        + "<BODY>" + ex.Message + "</BODY></HTML>\n";

                    p_response.BodyData = Encoding.UTF8.GetBytes(_bodyString);
                }
            }
            else
            {
                string _filepath = (String.Format(@"{0}\{1}", WebFolder, p_request.URL.Replace("/", @"\"))).Replace(@"\\", @"\");

                if (Directory.Exists(_filepath) == true)
                {
                    if (File.Exists(_filepath + DefaultPage) == true)
                    {
                        _filepath = Path.Combine(_filepath, DefaultPage);
                    }
                    else
                    {
                        string[] _folders = Directory.GetDirectories(_filepath);
                        string[] _files = Directory.GetFiles(_filepath);

                        string _bodyString
                            = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">\n"
                            + "<HTML><HEAD>\n"
                            + "<META http-equiv=Content-Type content=\"text/html; charset=UTF-8\">\n"
                            + "</HEAD>\n"
                            + "<BODY><p>Folder listing, to do not see this add a '" + DefaultPage + "' document\n<p>\n";

                        for (int i = 0; i < _folders.Length; i++)
                            _bodyString += String.Format("<br><a href = \"{0}{1}/\">[{1}]</a>\n", p_request.URL, Path.GetFileName(_folders[i]));

                        for (int i = 0; i < _files.Length; i++)
                            _bodyString += String.Format("<br><a href = \"{0}{1}\">{1}</a>\n", p_request.URL, Path.GetFileName(_files[i]));

                        _bodyString += "</BODY></HTML>\n";

                        p_response.BodyData = Encoding.UTF8.GetBytes(_bodyString);
                        return;
                    }
                }

                if (File.Exists(_filepath) == true)
                {
                    RegistryKey _regkey = Registry.ClassesRoot.OpenSubKey(Path.GetExtension(_filepath), false);

                    // Get the data from a specified item in the key.
                    string _type = (string)_regkey.GetValue("Content Type");

                    // Open the stream and read it back.
                    p_response.Content = File.Open(_filepath, FileMode.Open, FileAccess.Read);
                    if (String.IsNullOrEmpty(_type) == false)
                        p_response.Headers["Content-type"] = _type;
                }
                else
                {
                    p_response.Status = (int)ResponseState.NOT_FOUND;

                    string _bodyString
                        = "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">\n"
                        + "<HTML><HEAD>\n"
                        + "<META http-equiv=Content-Type content=\"text/html; charset=UTF-8\">\n"
                        + "</HEAD>\n"
                        + "<BODY>File not found!!</BODY></HTML>\n";

                    p_response.BodyData = Encoding.UTF8.GetBytes(_bodyString);
                }
            }
        }

        //-------------------------------------------------------------------------------------------------------------------------
        // 
        //-------------------------------------------------------------------------------------------------------------------------
    }
}