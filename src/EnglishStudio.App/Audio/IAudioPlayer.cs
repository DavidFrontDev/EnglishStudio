namespace EnglishStudio.App.Audio;

public interface IAudioPlayer
{
    void Play(string filePath);
    void Stop();
}
