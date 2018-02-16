using System;

namespace GainFrameWork.Communication
{
    public static class EventsHelper
    {
        public static void Fire(Delegate del, params object[] args)
        {
            if (del==null)
            {
                return;
            }
            Delegate[] delegates = del.GetInvocationList();
            foreach(Delegate sink in delegates)
            {
                try
                {
                    sink.DynamicInvoke(args);
                }
                catch (Exception)
                {
                   //log
                }
            }
        }
    }
}
