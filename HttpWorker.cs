using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System;
using System.Net.Http;
using Newtonsoft.Json;

public class HttpWorker
{
    #region Define

    [System.Serializable]
    public struct HttpWorkerResult
    {
        public string Text;
        public string NickName;
        public DateTime TimeLine;
        public int RND;
    }

    public class LimitedQueue<T> : Queue<T>
    {
        public int Limit { get; set; }

        public LimitedQueue(int limit) : base(limit)
        {
            Limit = limit;
        }

        public new void Enqueue(T item)
        {
            while (Count >= Limit)
            {
                Dequeue();
            }
            base.Enqueue(item);
        }

        public bool IsFull
        {
            get
            {
                return Count == Limit;
            }
        }
    }

    #endregion

    private int m_httpMaxWorkCount = 128;
    private int m_httpMaxResultCount = 256;
    private LimitedQueue<HttpWorkerResult> m_httpWorkerResults;

    private int m_roomID;
    private bool m_working;

    public void StartWorking(int roomID)
    {
        if (roomID <= 0)
        {
            return;
        }

        m_httpWorkDeses = new Queue<HttpWorkDes>(m_httpMaxWorkCount);
        m_stringWorkDeses = new Queue<StringWorkDes>(m_httpMaxWorkCount);
        m_httpWorkerResults = new LimitedQueue<HttpWorkerResult>(m_httpMaxResultCount);


        m_httpThread = new Thread(new ThreadStart(HttpWorkerThreadEntry));
        m_httpThread.Start();

        m_stringThread = new Thread(new ThreadStart(StringWorkerThreadEntry));
        m_stringThread.Start();

        m_roomID = roomID;
        m_working = true;
    }

    public void StopWorking()
    {
        if (m_httpThread != null)
        {
            m_httpThread.Abort();
            m_httpThread = null;
        }
        if (m_stringThread != null)
        {
            m_stringThread.Abort();
            m_stringThread = null;
        }
        m_httpWorkDeses = null;
        m_stringWorkDeses = null;
        m_httpWorkerResults = null;

        m_working = false;
    }

    public void GiveOneWork()
    {
        if (!m_working)
        {
            return;
        }

        HttpWorkDes work = new HttpWorkDes();
        Dictionary<string, string> asks = new Dictionary<string, string>() {
            { "roomid", m_roomID.ToString() },
            { "csrf_token", "" },
            { "csrf", "" },
            { "visit_id", "" },
        };
        work.RequestContent = new FormUrlEncodedContent(asks);

        lock (m_httpWorkDeses)
        {
            m_httpWorkDeses.Enqueue(work);
        }
    }

    public void EnqueueFakeResult(HttpWorkerResult result)
    {
        if (m_httpWorkerResults == null)
        {
            m_httpWorkerResults = new LimitedQueue<HttpWorkerResult>(m_httpMaxResultCount);
        }

        m_httpWorkerResults.Enqueue(result);
    }

    public HttpWorkerResult[] ConsumeAll()
    {
        if (m_httpWorkerResults == null)
        {
            return null;
        }

        lock (m_httpWorkerResults)
        {
            HttpWorkerResult[] output = new HttpWorkerResult[m_httpWorkerResults.Count];
            //foreach (var result in m_httpWorkerResults)
            //{
            //    print($"{result.NickName} at {result.TimeLine}: {result.Text}");
            //}
            int counter = 0;
            foreach (var result in m_httpWorkerResults)
            {
                output[counter++] = result;
            }
            m_httpWorkerResults.Clear();
            return output;
        }
    }

    #region Http Worker Thread

    private Thread m_httpThread;

    private const int c_HttpWorkerThreadSleepTime = 500;
    private struct HttpWorkDes
    {
        public FormUrlEncodedContent RequestContent;
    }

    private Queue<HttpWorkDes> m_httpWorkDeses;

    private void HttpWorkerThreadEntry()
    {
        HttpClient client = new HttpClient();
        List<HttpWorkDes> works = new List<HttpWorkDes>();
        List<StringWorkDes> results = new List<StringWorkDes>();

        while (true)
        {
            // See if there is any job
            lock (m_httpWorkDeses)
            {
                foreach (var work in m_httpWorkDeses)
                {
                    works.Add(work);
                }
                m_httpWorkDeses.Clear();
            }

            if (works.Count == 0)
            {
                Thread.Sleep(c_HttpWorkerThreadSleepTime);
            }
            else
            {
                foreach (var work in works)
                {
                    HttpResponseMessage httpMsg = client.PostAsync("https://api.live.bilibili.com/xlive/web-room/v1/dM/gethistory", work.RequestContent).Result;
                    StringWorkDes result = new StringWorkDes();
                    result.HttpResponse = httpMsg;
                    results.Add(result);
                }
                works.Clear();

                lock (m_stringWorkDeses)
                {
                    foreach (var result in results)
                    {
                        m_stringWorkDeses.Enqueue(result);
                    }
                }
                results.Clear();
            }
        }
    }

    #endregion

    #region String Analysis

    private Thread m_stringThread;

    private const int c_StringWorkerThreadSleepTime = 500;

    private HashSet<int> m_proccessRndIDs = new HashSet<int>();
    private HashSet<int> m_cacheRndTb = new HashSet<int>();

    private struct CustomRndStruct
    {
        public string Name;
        public string SendTime;
    }

    private struct StringWorkDes
    {
        public HttpResponseMessage HttpResponse;
    }

    [System.Serializable]
    private struct ResponseMessageJsonOneMsg
    {
        public string text;
        public int uid;
        public string nickname;
        public string timeline;
        public string rnd;
    }

    [System.Serializable]
    private struct ResponseMessageJsonData
    {
        public List<ResponseMessageJsonOneMsg> admin;
        public List<ResponseMessageJsonOneMsg> room;
    }

    [System.Serializable]
    private struct ResponseMessageJson
    {
        public int code;
        public ResponseMessageJsonData data;
    }

    private Queue<StringWorkDes> m_stringWorkDeses;
    private DateTime m_lastResultDateTime;

  
    private void StringWorkerThreadEntry()
    {
        List<StringWorkDes> works = new List<StringWorkDes>();
        List<HttpWorkerResult> results = new List<HttpWorkerResult>();

        // Init matchers
        // Work out some string matches here.

        while (true)
        {
            // See if there is any job
            lock (m_stringWorkDeses)
            {
                foreach (var work in m_stringWorkDeses)
                {
                    works.Add(work);
                }
                m_stringWorkDeses.Clear();
            }

            if (works.Count == 0)
            {
                Thread.Sleep(c_StringWorkerThreadSleepTime);
            }
            else
            {
                Dictionary<int, int> RNDOuter = new Dictionary<int, int>();
                foreach (var work in works)
                {
                    string message = work.HttpResponse.Content.ReadAsStringAsync().Result;
                    ResponseMessageJson finalJson;
                    try
                    {
                        finalJson = JsonConvert.DeserializeObject<ResponseMessageJson>(message);
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }

                    DateTime lastestTimeInMsgBoxes = new DateTime();
                    List<ResponseMessageJsonOneMsg>[] msgBoxes = new List<ResponseMessageJsonOneMsg>[] { finalJson.data.admin, finalJson.data.room };
                    int msgBoxesLength = msgBoxes.Length;
                    for (int x = 0; x < msgBoxesLength; x++)
                    {
                        List<ResponseMessageJsonOneMsg> msgBox = msgBoxes[x];
                        if (msgBox != null)
                        {
                            foreach (var data in msgBox)
                            {
                                CustomRndStruct rndStruct;
                                rndStruct.Name = data.nickname;
                                rndStruct.SendTime = data.timeline;
                                int customRND = rndStruct.GetHashCode();

                                m_cacheRndTb.Add(customRND);
                                if (m_proccessRndIDs.Contains(customRND))
                                {
                                    // Already processed
                                    continue;
                                }

                                HttpWorkerResult result = new HttpWorkerResult();

                                bool success = DateTime.TryParse(data.timeline, out result.TimeLine);
                                if (!success)
                                {
                                    //Debug.LogWarning($"Time is invalid! {data.timeline} {message}");
                                    continue;
                                }

                                result.Text = data.text;
                                result.NickName = data.nickname;
                                results.Add(result);
                            }
                        }
                    }

                    var tmp = m_cacheRndTb;
                    m_cacheRndTb = m_proccessRndIDs;
                    m_proccessRndIDs = tmp;
                    m_cacheRndTb.Clear();
                }
                works.Clear();

                lock (m_httpWorkerResults)
                {
                    foreach (var result in results)
                    {
                        if (m_httpWorkerResults.IsFull)
                        {
                            //Debug.LogWarning("Result is dequing! Consider consume more frequently!");
                        }
                        //print($"{result.NickName} {result.Text} {m_httpWorkerResults.Count} {m_httpWorkerResults.Limit}");
                        m_httpWorkerResults.Enqueue(result);
                    }
                }
                results.Clear();
            }
        }
    }

    #endregion
}
