namespace Launcher.Core;

/// <summary>
/// What the launcher's one output plays next, block by block (<see cref="SoundLine"/>, on its thread only): the sound
/// started last, from its start, and silence once it has ended, so that the output never stops between two sounds.
/// A sound started while another plays cuts it short, as before; the one cut short fades out under the new one's
/// start over <see cref="Fade"/> samples instead of stopping dead (no click).
/// </summary>
public sealed class SoundMix
{
    /// <summary>Samples over which a sound cut short fades out: 4 ms at the line's rate.</summary>
    public const int Fade = SoundLine.Rate * 4 / 1000;

    private short[]? _sound;    // what plays
    private int _at;
    private short[]? _fading;   // what was cut short, fading out under it
    private int _fadingAt;
    private int _faded;

    /// <summary>Whether a sound still plays (or one cut short still fades out).</summary>
    public bool Playing => _sound is not null || _fading is not null;

    /// <summary>Starts the samples (at the line's rate) from their start, cutting short what plays.</summary>
    public void Start(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (_sound is not null)
        {
            _fading = _sound;
            _fadingAt = _at;
            _faded = 0;
        }
        _sound = samples.Length > 0 ? samples : null;
        _at = 0;
    }

    /// <summary>Nothing plays any more (a sound no output could take).</summary>
    public void Stop()
    {
        _sound = null;
        _fading = null;
    }

    /// <summary>The block filled with what plays next, silence where nothing does.</summary>
    public void Fill(Span<short> block)
    {
        for (var i = 0; i < block.Length; i++)
        {
            var sum = 0;
            if (_sound is { } sound)
            {
                sum = sound[_at++];
                if (_at >= sound.Length)
                {
                    _sound = null;
                }
            }
            if (_fading is { } fading)
            {
                sum += fading[_fadingAt++] * (Fade - _faded) / Fade;
                _faded++;
                if (_fadingAt >= fading.Length || _faded >= Fade)
                {
                    _fading = null;
                }
            }
            block[i] = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
        }
    }
}
