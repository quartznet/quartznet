namespace Quartz.Web.Api.Dto
{
    public class ServerHeaderDto
    {
        public ServerHeaderDto(Server server)
        {
            Name = server.Name;
            Address = server.Address;
        }

        public string Name { get; private set; }
        public string Address { get; private set; }
    }
}