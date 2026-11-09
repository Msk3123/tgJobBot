using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Core.Abstractions;

/// <summary>Turns a (tailored) CV into a one-page PDF.</summary>
public interface ICvRenderer
{
    byte[] Render(CvData cv);
}
