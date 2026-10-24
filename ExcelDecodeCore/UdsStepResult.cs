using System;
using System.Collections.Generic;
using ExcelDecodeCore;

namespace Jabil.Uds.Core
{
    public class UdsStepResult
    {
        public StepResult Step = new StepResult();
        public bool Success;
        public string Error = "";
        public string ResultText = "FAIL";
        public List<string> CommandHex = new List<string>();
        public List<string> ResponseHexList = new List<string>();
        public string CombinedResponseHex = "";
        public byte[] CombinedResponse = new byte[0];
        public DecodeResult Decode;
        public List<string> ActualRequestHex = new List<string>();
        public string Trace = "";
        public string FirstName = "";
        public object FirstRawValue;
        public object FirstValue;
        public string FirstUnit = "";

        public string ToReportText()
        {
            var sb = new System.Text.StringBuilder();
            string requestText = string.Join(" | ", ActualRequestHex);
            if (string.IsNullOrEmpty(requestText)) requestText = string.Join(" | ", CommandHex);
            sb.AppendLine("Request:  " + requestText);
            string responseText = string.Join(" | ", ResponseHexList);
            if (string.IsNullOrEmpty(responseText)) responseText = CombinedResponseHex;
            sb.AppendLine("Response: " + responseText);
            if (Decode != null && Decode.Measurements.Count > 0)
            {
                foreach (DecodedMeasurement measurement in Decode.Measurements)
                {
                    string value = measurement.Value == null ? "" : Convert.ToString(measurement.Value, System.Globalization.CultureInfo.InvariantCulture);
                    sb.AppendLine(string.Format("{0} = {1} {2}  [raw: {3}] {4}",
                        measurement.Name, value, measurement.Unit, measurement.RawHex,
                        measurement.Passed ? "PASS" : "FAIL"));
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
