using System.Threading;
using System.Threading.Tasks;


namespace Blinky
{
    public delegate void ParameterizedWorkerThreadStart(object status);

    public class WorkerThread
    {
        private ParameterizedWorkerThreadStart workerCallback;
        private object status;

        public WorkerThread(ParameterizedWorkerThreadStart workerThread)
        {
            this.workerCallback = workerThread;
        }

        public void Start(object status)
        {
            Task.Run(() =>
            {
                this.status = status;
                this.InternalThread();
            });
        }

        private void InternalThread()
        {
            this.workerCallback(this.status);
        }
    }
}
