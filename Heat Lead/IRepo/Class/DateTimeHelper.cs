namespace Heat_Lead.IRepo.Class
{
    public static class DateTimeHelper
    {
        public static DateTime? ConvertIsoStringToDateTime(string isoDateString)
        {
            if (string.IsNullOrEmpty(isoDateString))
            {
                return null;
            }

            if (DateTime.TryParse(isoDateString, out DateTime date))
            {
                return date;
            }

            return null;
        }
    }
}
