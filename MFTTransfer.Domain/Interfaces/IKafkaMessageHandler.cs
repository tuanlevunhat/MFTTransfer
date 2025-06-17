using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Interfaces
{
    public interface IKafkaMessageHandler
    {
        string Topic { get; }
        string GroupId { get; }
        Task HandleAsync(string message, CancellationToken cancellationToken);
    }

}
