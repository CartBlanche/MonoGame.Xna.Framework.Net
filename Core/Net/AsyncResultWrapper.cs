namespace Microsoft.Xna.Framework.Net
{
	/// <summary>
	/// Wrapper for async operations to provide MonoGame compatible IAsyncResult interface.
	/// </summary>
	internal class AsyncResultWrapper<T> : IAsyncResult
    {
        private readonly Task<T> task;
        private readonly object asyncState;

        public AsyncResultWrapper(Task<T> task, AsyncCallback callback, object asyncState)
        {
            this.task = task;
            this.asyncState = asyncState;

            if (callback != null)
            {
                task.ContinueWith(t => callback(this));
            }
        }

        public object AsyncState => asyncState;

        public System.Threading.WaitHandle AsyncWaitHandle => ((IAsyncResult)task).AsyncWaitHandle;

        // the callback always runs on a continuation, never inside Begin
        public bool CompletedSynchronously => false;

        public bool IsCompleted => task.IsCompleted;

        public T GetResult()
        {
            return task.GetAwaiter().GetResult();   // rethrows the original exception, not an AggregateException
        }
    }
}
