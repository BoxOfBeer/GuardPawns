using System;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using SpaceDNA;

namespace SpaceDNA.Audio
{
    /// <summary>
    /// Procedural audio engine for planet "breathing" sounds.
    /// Uses pink/brown noise modulated by planet parameters.
    /// </summary>
    public class PlanetAudio : IDisposable
    {
        private ALDevice _device;
        private ALContext _context;
        private int _source;
        private int[]? _buffers;
        private const int BufferCount = 4;
        private const int BufferSize = 4096;
        private const int SampleRate = 22050;
        
        private float _volume = 0.3f;
        private bool _isEnabled = true;
        private float _temperature = 0.5f;
        private float _atmosphere = 0.5f;
        private float _geologicActivity = 1.0f;
        private int _seed = 0;
        
        private Random _noiseRandom = new Random();
        private float _phase = 0f;
        private float _lowpassState = 0f;
        private float _brownNoiseState = 0f;
        
        public float Volume 
        { 
            get => _volume; 
            set 
            { 
                _volume = Math.Clamp(value, 0f, 1f);
                UpdateVolume();
            }
        }
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (_source != 0)
                {
                    if (_isEnabled)
                    {
                        AL.SourcePlay(_source);
                        GameLog.Log("Audio: Source started");
                    }
                    else
                    {
                        AL.SourcePause(_source);
                        GameLog.Log("Audio: Source paused");
                    }
                }
            }
        }
        
        public PlanetAudio()
        {
            Initialize();
        }
        
        private void Initialize()
        {
            try
            {
                // Open default audio device
                _device = ALC.OpenDevice(null);
                if (_device == ALDevice.Null)
                {
                    GameLog.Log("Audio: Failed to open device");
                    return;
                }
                
                _context = ALC.CreateContext(_device, new int[0]);
                if (_context == ALContext.Null)
                {
                    GameLog.Log("Audio: Failed to create context");
                    return;
                }
                
                ALC.MakeContextCurrent(_context);
                
                // Create source and buffers
                _source = AL.GenSource();
                _buffers = AL.GenBuffers(BufferCount);
                
                // Configure source
                AL.Source(_source, ALSourcef.Gain, _volume * 0.5f);
                AL.Source(_source, ALSourceb.Looping, false);
                
                // Generate initial noise seed
                _noiseRandom = new Random(_seed);
                
                // Queue initial buffers
                for (int i = 0; i < BufferCount; i++)
                {
                    GenerateNoiseBuffer(_buffers[i]);
                }
                
                AL.SourceQueueBuffers(_source, BufferCount, _buffers);
                AL.SourcePlay(_source);
                
                GameLog.Log("Audio: Initialized successfully");
            }
            catch (Exception ex)
            {
                GameLog.Log($"Audio: Initialization error: {ex.Message}");
            }
        }
        
        public void UpdateParameters(float temperature, float atmosphere, float geologicActivity, int seed)
        {
            _temperature = temperature;
            _atmosphere = atmosphere;
            _geologicActivity = geologicActivity;
            
            if (seed != _seed)
            {
                _seed = seed;
                _noiseRandom = new Random(seed);
            }
        }
        
        private void GenerateNoiseBuffer(int buffer)
        {
            short[] samples = new short[BufferSize];
            
            for (int i = 0; i < BufferSize; i++)
            {
                float sample = GeneratePlanetSample();
                samples[i] = (short)(sample * short.MaxValue * 0.3f);
            }
            
            AL.BufferData(buffer, ALFormat.Mono16, samples, SampleRate);
        }
        
        private float GeneratePlanetSample()
        {
            // Base brown noise (random walk)
            float white = (float)(_noiseRandom.NextDouble() * 2.0 - 1.0);
            _brownNoiseState = _brownNoiseState * 0.995f + white * 0.05f;
            
            // Pink noise component (1/f noise)
            float pink = GeneratePinkNoise();
            
            // Low frequency rumble for geological activity
            _phase += 0.0001f * (1f + _geologicActivity * 2f);
            float rumble = MathF.Sin(_phase * MathF.PI * 2f) * _geologicActivity * 0.3f;
            
            // Add harmonics for more complex rumble
            rumble += MathF.Sin(_phase * MathF.PI * 4f) * _geologicActivity * 0.15f;
            rumble += MathF.Sin(_phase * MathF.PI * 0.5f) * _geologicActivity * 0.2f;
            
            // Wind noise (high frequency, more prominent without atmosphere)
            float windIntensity = (1f - _atmosphere) * 0.4f;
            float wind = white * windIntensity;
            
            // Apply lowpass filter based on atmosphere
            // Dense atmosphere = muffled sound
            float cutoff = 0.1f + _atmosphere * 0.3f;
            _lowpassState = _lowpassState + cutoff * (wind - _lowpassState);
            wind = _lowpassState;
            
            // Temperature affects the "color" of the sound
            // Hot = more high frequencies, Cold = more low frequencies
            float tempMod = (_temperature - 0.5f) * 0.3f;
            
            // Mix components
            float output = _brownNoiseState * 0.3f;  // Base rumble
            output += pink * (0.2f + tempMod);        // Pink noise
            output += rumble * 0.4f;                   // Geological activity
            output += wind * 0.3f;                     // Wind
            
            // Apply atmosphere damping
            output *= (0.3f + _atmosphere * 0.7f);
            
            // Add subtle "breathing" modulation
            float breathPhase = _phase * 0.1f;
            float breathMod = 0.8f + 0.2f * MathF.Sin(breathPhase);
            output *= breathMod;
            
            return Math.Clamp(output, -1f, 1f);
        }
        
        private float _pinkB0, _pinkB1, _pinkB2, _pinkB3, _pinkB4, _pinkB5, _pinkB6 = 0f;
        
        private float GeneratePinkNoise()
        {
            // Paul Kellet's pink noise algorithm
            float white = (float)(_noiseRandom.NextDouble() * 2.0 - 1.0);
            
            _pinkB0 = 0.99886f * _pinkB0 + white * 0.0555179f;
            _pinkB1 = 0.99332f * _pinkB1 + white * 0.0750759f;
            _pinkB2 = 0.96900f * _pinkB2 + white * 0.1538520f;
            _pinkB3 = 0.86650f * _pinkB3 + white * 0.3104856f;
            _pinkB4 = 0.55000f * _pinkB4 + white * 0.5329522f;
            _pinkB5 = -0.7616f * _pinkB5 - white * 0.0168980f;
            
            return (_pinkB0 + _pinkB1 + _pinkB2 + _pinkB3 + _pinkB4 + _pinkB5 + _pinkB6 + white * 0.5362f) * 0.11f;
        }
        
        private void UpdateVolume()
        {
            if (_source != 0)
            {
                AL.Source(_source, ALSourcef.Gain, _volume * 0.85f);
            }
        }
        
        public void Update()
        {
            if (!_isEnabled || _source == 0) return;
            
            // Check for processed buffers
            AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
            
            if (processed > 0)
            {
                // Unqueue and requeue buffers
                int[] buffers = new int[processed];
                AL.SourceUnqueueBuffers(_source, processed, buffers);
                
                foreach (int buf in buffers)
                {
                    GenerateNoiseBuffer(buf);
                }
                
                AL.SourceQueueBuffers(_source, processed, buffers);
                
                // Ensure playback continues
                AL.GetSource(_source, ALGetSourcei.SourceState, out int state);
                if ((ALSourceState)state != ALSourceState.Playing)
                {
                    AL.SourcePlay(_source);
                }
            }
        }
        
        public void Dispose()
        {
            if (_source != 0)
            {
                AL.SourceStop(_source);
                AL.DeleteSource(_source);
            }
            
            if (_buffers != null)
            {
                AL.DeleteBuffers(_buffers);
            }
            
            if (_context != ALContext.Null)
            {
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
            }
            
            if (_device != ALDevice.Null)
            {
                ALC.CloseDevice(_device);
            }
        }
    }
}
