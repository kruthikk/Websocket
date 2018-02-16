using System;

namespace GainFrameWork.Communication
{
    public delegate void ErrorEventHandler(object sender, ErrorEventArgs e);

    public class ErrorEventArgs:EventArgs
    {
        public int ErrorCode { get; private set; }
        public string Description { get; private set; }

        public ErrorEventArgs(int errorcode, string errorDescription)
        {
            ErrorCode = errorcode;
            Description = errorDescription;
        }

    }
}
