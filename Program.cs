

namespace SimpleProxy{
    using System;
    using System.Collections;
    using System.Collections.Specialized;
    using System.IO;
    using System.Net;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Configuration;
    using System.Collections.Generic;

    class Program{
        static string port;
        static string host;

        static void Main(){
            port = ConfigurationManager.AppSettings["proxy_port"];
            host = ConfigurationManager.AppSettings["proxy_host"];

            //http server listener
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://" + host + ":" + port + "/");
            listener.Start();
            Console.WriteLine("Listening on " + host + ":" + port);

            //main server loop
            while (true){
			
                //as soon as there is a connection request
                HttpListenerContext ctx = listener.GetContext();
                new Thread(new Worker(ctx).ProcessRequest).Start();
            }
        }

    }

    class Worker{

        HttpListenerContext context;
        WebProxy parent;
        //service  port
        string port;
        string host;
        //pass through headers
        string[] headers = new string[] { "Cookie", "Accept", "Referrer", "Accept-Language" };



        public Worker(HttpListenerContext context){
            this.context = context;

            port = ConfigurationManager.AppSettings["proxy_port"];
            host = ConfigurationManager.AppSettings["proxy_host"];

            //init proxy
            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["parent_host"]) && !string.IsNullOrEmpty(ConfigurationManager.AppSettings["parent_port"]))
            {
                parent = new WebProxy(ConfigurationManager.AppSettings["parent_host"], int.Parse(ConfigurationManager.AppSettings["parent_port"]));

                if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["parent_user"]))
                {
                    parent.Credentials = new NetworkCredential(ConfigurationManager.AppSettings["parent_user"], ConfigurationManager.AppSettings["parent_pass"], ConfigurationManager.AppSettings["parent_domain"]);
                }
                else
                {
                    parent.UseDefaultCredentials = true;
                }
            }
            else
            {
                parent = null;
            }

        }

        private byte[] GetBytesFromStream(Stream stream)
        {
            byte[] result;
            byte[] buffer = new byte[256];

            BinaryReader reader = new BinaryReader(stream);
            MemoryStream memoryStream = new MemoryStream();

            int count = 0;
            while (true)
            {
                count = reader.Read(buffer, 0, buffer.Length);
                memoryStream.Write(buffer, 0, count);

                if (count == 0)
                    break;
            }

            result = memoryStream.ToArray();
            memoryStream.Close();
            reader.Close();
            stream.Close();

            return result;
        }

        public void ProcessRequest()
        {
            //request console log
            string url = context.Request.Url.ToString().Replace(":" + port, "");
            string msg = DateTime.Now.ToString("hh:mm:ss") + " " + context.Request.HttpMethod + " " + context.Request.Url.Host.ToString();
            Console.WriteLine(msg);

            byte[] result;
            try
            {
                WebRequest request = WebRequest.Create(url);

                //config proxy
                if (parent!=null)
                    request.Proxy = parent;

                

                //addo le chiavi nella testa
                request.Method = context.Request.HttpMethod;
                request.ContentType = context.Request.ContentType;
                request.ContentLength = context.Request.ContentLength64;
                if (context.Request.ContentLength64 > 0 && context.Request.HasEntityBody)
                {
                    using (System.IO.Stream body = context.Request.InputStream)
                    {
                        byte[] requestdata = GetBytesFromStream(body);
                        request.ContentLength = requestdata.Length;
                        Stream s = request.GetRequestStream();
                        s.Write(requestdata, 0, requestdata.Length);
                        s.Close();
                    }
                }               
                

                //request processing
                WebResponse response = request.GetResponse();
                result = GetBytesFromStream(response.GetResponseStream());
                context.Response.ContentType = response.ContentType;

                response.Close();
                               


            }
            catch (WebException wex)
            {
                //exception handler (404,407...)
                result = Encoding.UTF8.GetBytes(wex.Message);
                HttpWebResponse resp = (HttpWebResponse)wex.Response;
                context.Response.StatusCode = (int)resp.StatusCode;
                context.Response.StatusDescription = resp.StatusDescription;
                Console.WriteLine("ERROR:" + wex.Message);
            }
            catch (Exception ex)
            {
                result = Encoding.UTF8.GetBytes(ex.Message);
                Console.WriteLine("ERROR:" + ex.Message);
            }

            //response
            byte[] b = result;
            context.Response.ContentLength64 = b.Length;
            context.Response.OutputStream.Write(b, 0, b.Length);
            context.Response.OutputStream.Close();
        }
    }
}

