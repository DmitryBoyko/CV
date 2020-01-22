using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASKService
{
    public static class ThreadManager
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static void Init(bool logging)
        {
            LOGGING = logging;
            ClearThreads();
        }

        #region Logging

        private static bool LOGGING = false;

       

        #endregion

        readonly static Dictionary<int, BindedThread> Threads = new Dictionary<int, BindedThread>();

        private static DateTime lastClean;

        private static void AddThread(Thread thr, string name, object owner, int debugId, int tid)
        {
            if (thr.Name == null)
                thr.Name = name;
            if (lastClean < DateTime.Now.AddMinutes(-5))
                ClearThreads();

            BindedThread bth = new BindedThread(thr, name, owner, debugId, tid);
            lock (Threads)
            {
                if (!Threads.ContainsKey(thr.ManagedThreadId))
                    Threads.Add(thr.ManagedThreadId, bth);
                else
                    Threads[thr.ManagedThreadId] = bth;
            }
        }

        /// <summary>
        /// Регистрирует текущий поток с указанным именем
        /// </summary>
        /// <param name="name">Имя потока</param>
        public static void RegisterThread(string name)
        {
#pragma warning disable CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
            AddThread(Thread.CurrentThread, name, null, -1, AppDomain.GetCurrentThreadId());
#pragma warning restore CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
        }

        /// <summary>
        /// Регистрирует текущий поток с указанным именем
        /// </summary>
        /// <param name="name">Имя потока</param>
        public static void RegisterThreadId(string name, int id)
        {
#pragma warning disable CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
            AddThread(Thread.CurrentThread, name, null, id, AppDomain.GetCurrentThreadId());
#pragma warning restore CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
        }

        /// <summary>
        /// Регистрирует текущий поток с указанным именем и тэгом
        /// </summary>
        /// <param name="name">Имя потока</param>
        /// <param name="tag">Тэг потока</param>
        public static void RegisterThread(string name, object tag)
        {
#pragma warning disable CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
            AddThread(Thread.CurrentThread, name, tag, -1, AppDomain.GetCurrentThreadId());
#pragma warning restore CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
        }

        /// <summary>
        /// Регистрирует текущий поток с указанным именем и тэгом
        /// </summary>
        /// <param name="name">Имя потока</param>
        /// <param name="tag">Тэг потока</param>
        public static void RegisterThread(string name, object tag, int id)
        {
#pragma warning disable CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
            AddThread(Thread.CurrentThread, name, tag, id, AppDomain.GetCurrentThreadId());
#pragma warning restore CS0618 // 'AppDomain.GetCurrentThreadId()' is obsolete: 'AppDomain.GetCurrentThreadId has been deprecated because it does not provide a stable Id when managed threads are running on fibers (aka lightweight threads). To get a stable identifier for a managed thread, use the ManagedThreadId property on Thread.  http://go.microsoft.com/fwlink/?linkid=14202'
        }

        /// <summary>
        /// Регистрирует указанный поток
        /// </summary>
        /// <param name="thread">Поток для регистрации</param>
        public static void RegisterThread(Thread thread, int tid)
        {
            if (thread == null)
                return;
            AddThread(thread, thread.Name, null, -1, tid);
        }

        /// <summary>
        /// Разрегистрирует текущий поток
        /// </summary>
        public static void UnregisterThread()
        {
            UnregisterThread(Thread.CurrentThread);
        }

        /// <summary>
        /// Разрегистрирует указанный поток
        /// </summary>
        /// <param name="thread">Поток для разрегистрации</param>
        public static void UnregisterThread(Thread thread)
        {
            if (thread == null) return;

            lock (Threads)
            {
                Threads.Remove(thread.ManagedThreadId);
            }
        }

        private static void ClearThreads()
        {
            lastClean = DateTime.Now;
            lock (Threads)
            {
                List<int> keys = new List<int>(Threads.Keys);
                foreach (int k in keys)
                {
                    BindedThread bt = Threads[k];
                    if (bt.Thread == null || bt.Thread.ThreadState == System.Threading.ThreadState.Stopped)
                    {
                        Threads.Remove(k);
                    }
                }
            }
        }

        /// <summary>
        /// Возвращает список зарегистрированных потоков
        /// </summary>
        /// <returns></returns>
        public static List<BindedThread> GetActiveThreads()
        {
            ClearThreads();
            lock (Threads)
            {
                return new List<BindedThread>(Threads.Values);
            }
        }

        public static BindedThread GetThread(int id)
        {
            lock (Threads)
            {
                return Threads.Values.FirstOrDefault(t => t.TID == id);
            }
        }

        /// <summary>
        /// Устанавливает идентификатор запроса для текущего потока
        /// </summary>
        /// <param name="id"></param>
        public static void SetQueryId(Guid id)
        {
            BindedThread bt = GetCurrent();
            if (bt != null)
                bt.QueryId = id;
        }

        /// <summary>
        /// Останавливает все потоки зарегестрированные с указанным тэгом
        /// </summary>
        /// <param name="tag">Владелец, чби потоки необходимо остановить</param>
        public static void KillThreadsByTag(object tag)
        {
            /*lock (Threads)
            {
                foreach (BindedThread thread in Threads.Values)
                {
                    if (thread.Tag == tag)
                        if (thread.Thread.ThreadState == ThreadState.Running || thread.Thread.ThreadState == ThreadState.WaitSleepJoin)
                            thread.Thread.Abort();
                }
            }

            ClearThreads();*/
        }

        /// <summary>
        /// Прерывает поток с указанным тэгом и идентификатором запроса
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="id"></param>
        public static void KillThreadsByQuery(object tag, Guid id)
        {
            /*lock (_threads)
            {
                foreach (BindedThread thread in _threads.Values)
                {
                    if (thread.QueryId.Equals(id) && thread.Tag == tag)
                        if (thread.Thread.ThreadState == ThreadState.Running || thread.Thread.ThreadState == ThreadState.WaitSleepJoin)
                            thread.Thread.Abort();
                }
            }

            ClearThreads();*/
        }

        private static BindedThread GetCurrent()
        {
            BindedThread bt = null;
            lock (Threads)
            {
                Threads.TryGetValue(Thread.CurrentThread.ManagedThreadId, out bt);
            }
            return bt;
        }

        public static void RenameCurrent(string name)
        {
            BindedThread bt = GetCurrent();
            if (bt != null)
            {
                bt.Name = name;
            }
        }

        public static int GetDebugId()
        {
            BindedThread bt = GetCurrent();
            if (bt != null) return bt.DebugId;
            return -1;
        }
    }

    /// <summary>
    /// Контейнер для хранения потока, его тэка и идентификатора запроса
    /// </summary>
    public class BindedThread
    {
        private Thread _thread;
        private string _name;
        private object _tag;
        private Guid _queryId;
        private int _debugId;
        private int _tid;

        /// <summary>
        /// Инициирует новый экземпляр BindedThread с указанным потоком, тэгом и системным идентификатором потока
        /// </summary>
        /// <param name="thread">Запущенный поток</param>
        /// <param name="name">Название потока</param>
        /// <param name="tag">Тэг потока</param>
        /// <param name="threadID">Системный идентификатор потока</param>
        public BindedThread(Thread thread, string name, object tag, int debugId, int tid)
        {
            _thread = thread;
            _name = name;
            _tag = tag;
            _debugId = debugId;
            _tid = tid;
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; }
        }

        /// <summary>
        /// Тэг
        /// </summary>
        public object Tag { get { return _tag; } }

        /// <summary>
        /// Поток
        /// </summary>
        public Thread Thread { get { return _thread; } }

        /// <summary>
        /// Идентификатор потока
        /// </summary>
        public Guid QueryId
        {
            get { return _queryId; }
            set { _queryId = value; }
        }

        public int DebugId { get { return _debugId; } }

        public int TID { get { return _tid; } }

        public string GetStack()
        {
            try
            {
#pragma warning disable CS0618 // 'Thread.Suspend()' is obsolete: 'Thread.Suspend has been deprecated.  Please use other classes in System.Threading, such as Monitor, Mutex, Event, and Semaphore, to synchronize Threads or protect resources.  http://go.microsoft.com/fwlink/?linkid=14202'
                _thread.Suspend();
#pragma warning restore CS0618 // 'Thread.Suspend()' is obsolete: 'Thread.Suspend has been deprecated.  Please use other classes in System.Threading, such as Monitor, Mutex, Event, and Semaphore, to synchronize Threads or protect resources.  http://go.microsoft.com/fwlink/?linkid=14202'
#pragma warning disable CS0618 // 'StackTrace.StackTrace(Thread, bool)' is obsolete: 'This constructor has been deprecated.  Please use a constructor that does not require a Thread parameter.  http://go.microsoft.com/fwlink/?linkid=14202'
                var trace = new System.Diagnostics.StackTrace(_thread, true);
#pragma warning restore CS0618 // 'StackTrace.StackTrace(Thread, bool)' is obsolete: 'This constructor has been deprecated.  Please use a constructor that does not require a Thread parameter.  http://go.microsoft.com/fwlink/?linkid=14202'
                return trace.ToString();
            }
            catch
            {
                return String.Empty;
            }
            finally
            {
#pragma warning disable CS0618 // 'Thread.Resume()' is obsolete: 'Thread.Resume has been deprecated.  Please use other classes in System.Threading, such as Monitor, Mutex, Event, and Semaphore, to synchronize Threads or protect resources.  http://go.microsoft.com/fwlink/?linkid=14202'
                _thread.Resume();
#pragma warning restore CS0618 // 'Thread.Resume()' is obsolete: 'Thread.Resume has been deprecated.  Please use other classes in System.Threading, such as Monitor, Mutex, Event, and Semaphore, to synchronize Threads or protect resources.  http://go.microsoft.com/fwlink/?linkid=14202'
            }
        }
    }
}
