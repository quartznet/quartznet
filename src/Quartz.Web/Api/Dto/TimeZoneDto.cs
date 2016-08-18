using System;

namespace Quartz.Web.Api.Dto
{
    public class TimeZoneDto
    {
        public TimeZoneDto(TimeZoneInfo timeZone)
        {
            Id = timeZone.Id;
            StandardName = timeZone.StandardName;
            DisplayName = timeZone.DisplayName;
        }

        public string Id { get; }
        public string StandardName { get; }
        public string DisplayName { get; }
    }
}