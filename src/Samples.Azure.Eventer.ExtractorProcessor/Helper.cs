using System;
using System.Text;

namespace Samples.Azure.Eventer.ExtractorProcessor
{
    public static class Helper
    {
        public static string GetTimeWithMileseconds(DateTime input)
        {
            StringBuilder retVal = new StringBuilder();
            retVal.Append(input.ToLongTimeString());
            retVal.Append(":");
            retVal.Append(input.Millisecond);
            return retVal.ToString();
        }
    }
}
