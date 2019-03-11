using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;

using Quartz.Impl;
using Quartz.Web.Api.Dto;

namespace Quartz.Web.Api
{
    /// <summary>
    /// Web API endpoint for scheduler information.
    /// </summary>
    public class ServerController : Controller
    {
        [HttpGet]
        [Route("api/servers")]
        public IList<ServerHeaderDto> AllServers()
        {
            var servers = ServerRepository.LookupAll();

            return servers.Select(x => new ServerHeaderDto(x)).ToList();
        }

        [HttpGet]
        [Route("api/server/{serverName}/details")]
        public async Task<ServerDetailsDto> ServerDetails(string serverName)
        {
            var schedulers = await SchedulerRepository.Instance.LookupAll().ConfigureAwait(false);
            return new ServerDetailsDto(schedulers);
        }
    }
}