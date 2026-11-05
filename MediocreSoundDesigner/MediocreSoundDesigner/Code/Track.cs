using NAudio.Wave;

namespace MediocreSoundDesigner;

public enum SampleRegion {
  BEGINNING_ONLY,
  END_ONLY,
  BEGINNING_AND_END,
  ANYWHERE,
}

public class Track : IDisposable {
  // Fields
  private string file;
  private TimeSpan duration;
  private float offset;
  private float timeScale;
  private int sampleRate;
  private float[] samples;
  private float pitch;
  private int bits;
  private int channels;
  private AudioFileReader fileReader;
  private WaveOut outputDevice;
  private Panel panContainer;
  private int drawTimescale;

  // properties
  public int DrawOffset { get; set; }
  public bool IsDisabled { get; private set; }
  public Panel PanDrawSamples { get; private set; }
  public float Volume { get; private set; }
  private string tempFilePath => Path.Combine(Path.GetTempPath(), "MediocreSoundDesigner", "temp.wav");
  public PlaybackState State => outputDevice?.PlaybackState ?? PlaybackState.Stopped;

  // ctor
  public Track(string file) {
    fileReader = new AudioFileReader(file);
    int sampleCount = (int)(fileReader.Length / 4);
    this.samples = new float[sampleCount];
    int samplesRead = ((ISampleProvider)fileReader).Read(samples);
    if (samplesRead < sampleCount) {
      Array.Resize(ref samples, samplesRead);
    }

    this.file = file;
    this.duration = fileReader.TotalTime;
    this.offset = 0;
    this.timeScale = 1.0f;
    this.Volume = 1.0f;
    this.pitch = 1.0f;
    this.bits = fileReader.WaveFormat.BitsPerSample;
    this.channels = fileReader.WaveFormat.Channels;
    this.sampleRate = fileReader.WaveFormat.SampleRate;
    IsDisabled = false;
  }
  public Track(float[] samples) {
    this.samples = samples;

    this.file = "";
    this.duration = TimeSpan.Zero;
    this.offset = 0;
    this.timeScale = 1.0f;
    this.Volume = 1.0f;
    this.pitch = 1.0f;
    this.bits = 32;
    this.channels = 1;
    this.sampleRate = 44100;
    IsDisabled = false;
  }
  public void LinkPanels(Panel panDrawSamples, Panel panContainer) {
    this.PanDrawSamples = panDrawSamples;
    this.panContainer = panContainer;
  }

  // edits
  public float[] CloneSamples() {
    return (float[])samples.Clone();
  }
  public void ReplaceSamples(float[] samples) {
    this.samples = samples;
  }
  public void Reverse() {
    for (int i = 0; i < samples.Length / 2; i++) {
      var temp = samples[i];
      samples[i] = samples[samples.Length - 1 - i];
      samples[samples.Length - 1 - i] = temp;
    }
  }
  public void Flip() {
    for (int i = 0; i < samples.Length; i++) {
      samples[i] *= -1;
    }
  }
  // Fade in method
  public void FadeIn(float durationSeconds){
    if (durationSeconds <= 0 || sampleRate <= 0 || samples.Length == 0)
      return;
    
    // Calculate how many audio frames the fade should cover
    int totalFrames = samples.Length / channels;
    int fadeFrames = Math.Min(totalFrames, (int)(durationSeconds * sampleRate));

    if (fadeFrames <= 1)
      return;

    // Gradually increase volume from 0% to 100%
    for (int frame = 0; frame < fadeFrames; frame++)
    {
      float multiplier = (float)frame / (fadeFrames - 1);

      for (int channel = 0; channel < channels; channel++)
      {
        int index = frame * channels + channel;
        samples[index] *= multiplier;
      }
    }
  }
  // Fade out method
  public void FadeOut(float durationSeconds){
    if (durationSeconds <= 0 || sampleRate <= 0 || samples.Length == 0)
      return;
    
    int totalFrames = samples.Length / channels;
    int fadeFrames = Math.Min(totalFrames, (int)(durationSeconds * sampleRate));

    if (fadeFrames <= 1)
      return;

    // Start the fade near the end of the track
    int startFrame = totalFrames - fadeFrames;

    // Gradually decrease volume from 100% to 0%
    for (int frame = 0; frame < fadeFrames; frame++)
    {
      float multiplier = 1.0f - (float)frame / (fadeFrames - 1);

      for (int channel = 0; channel < channels; channel++)
      {
        int index = (startFrame + frame) * channels + channel;
        samples[index] *= multiplier;
      }
    }
  }
  public void Trim() {
  }
  public void AlterTimeScale(float scale) {
  }
  public void SetVolume(float newVolume) {
    Volume = newVolume;
  }
  public void EliminateSilence(SampleRegion region, float minDuration, float thres) {
    float[] newSamples = CloneSamples();
    if (region is SampleRegion.BEGINNING_ONLY or SampleRegion.BEGINNING_AND_END) {
      int silentDurationInNumSamples = 0;
      for (int i = 0; i < newSamples.Length; i++) {
        if (Math.Abs(newSamples[i]) <= thres) {
          silentDurationInNumSamples++;
        }
        else {
          break;
        }
      }
      if ((float)silentDurationInNumSamples / sampleRate >= minDuration) {
        newSamples = newSamples[silentDurationInNumSamples..];
      }
    }
    if (region is SampleRegion.END_ONLY or SampleRegion.BEGINNING_AND_END) {
      int silentDurationInNumSamples = 0;
      for (int i = newSamples.Length - 1; i >= 0; i--) {
        if (Math.Abs(newSamples[i]) <= thres) {
          silentDurationInNumSamples++;
        }
        else {
          break;
        }
      }
      if ((float)silentDurationInNumSamples / sampleRate >= minDuration) {
        newSamples = newSamples[0..^silentDurationInNumSamples];
      }
    }
    if (region is SampleRegion.ANYWHERE) {
      int silentDurationInNumSamples = 0;
      int silentRegionStart = 0;
      int i;
      for (i = 0; i < newSamples.Length; i++) {
        if (Math.Abs(newSamples[i]) <= thres) {
          silentDurationInNumSamples++;
        }
        else {
          if ((float)silentDurationInNumSamples / sampleRate >= minDuration) {
            newSamples = [..newSamples[0..silentRegionStart], ..newSamples[i..]];
            i = 0;
          }
          silentRegionStart = i + 1;
          silentDurationInNumSamples = 0;
        }
      }
      if ((float)silentDurationInNumSamples / sampleRate >= minDuration) {
        newSamples = [.. newSamples[0..silentRegionStart], .. newSamples[i..]];
      }
    }
    samples = newSamples;
  }

  // play
  public void Play() {
    outputDevice?.Dispose();
    fileReader?.Dispose();
    SaveToTempFile();
    fileReader = new AudioFileReader(tempFilePath);
    outputDevice = new WaveOut();
    outputDevice.Init(fileReader);
    outputDevice.Play();
  }
  public void Pause() {
    outputDevice?.Pause();
  }
  public void Reset() {
  }
  public int GetPlayheadPos() {
    return (int)((outputDevice?.GetPosition() ?? 0) / 4);
  }
  public void ToggleDisabled() {
    IsDisabled = !IsDisabled;
  }

  // file i/o
  private void SaveToTempFile() {
    Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath));
    using var writer = new WaveFileWriter(tempFilePath, fileReader?.WaveFormat ?? new());
    writer.WriteSamples(GetAdjustedSamples(), 0, samples.Length);
  }
  public void SaveToFile(string path) {
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    using var writer = new WaveFileWriter(path, fileReader?.WaveFormat ?? new());
    writer.WriteSamples(GetAdjustedSamples(), 0, samples.Length);
  }
  public float[] GetAdjustedSamples() {
    float[] adjSamples = new float[samples.Length];
    for (int s = 0; s < samples.Length; s++) {
      adjSamples[s] = samples[s] * Volume;
    }
    float[] deadSpace = new float[DrawOffset * drawTimescale];
    for (int i = 0; i < DrawOffset; i++) {
      deadSpace[i] = 0.0f;
    }

    return [.. deadSpace, .. adjSamples];
  }

  // render
  public void ShiftSampleOver(int amount) {
    DrawOffset += amount;
    DrawOffset = Math.Max(0, DrawOffset);
  }
  public void DrawSamples(Graphics g, int drawTimescale) {
    if (PanDrawSamples is null) {
      return;
    }

    this.drawTimescale = drawTimescale;
    float heightAdj = PanDrawSamples.Height / 2;
    if (samples != null && samples.Length > 0) {
      int x = DrawOffset;
      Pen pen = new(IsDisabled ? Settings.Color.TrackSamples_Disabled : Settings.Color.TrackSamples_Enabled);
      List<Point> points = new();
      for (int s = 0; s < samples.Length; s += drawTimescale) {
        var sample = (int)(samples[s] * heightAdj * Volume);
        points.Add(new(x, PanDrawSamples.Height / 2));
        points.Add(new(x, PanDrawSamples.Height / 2 - sample));
        x++;
      }
      g.DrawLines(pen, points.ToArray());
      pen.Dispose();
    }
  }

  // dispose
  public void Dispose() {
    outputDevice?.Dispose();
    fileReader?.Dispose();
  }
}
