"""Procedural ambience, stingers and music for Genesis Valley (all original, CC0 by the project).
Loops are generated circularly (FFT noise + wrap-around event placement) so they repeat seamlessly."""
import numpy as np, subprocess, os, sys
from scipy import signal

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else '/data/stage/audio'
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(20240607)

# ------------------------------------------------------------------ helpers
def t_axis(sec): return np.arange(int(sec * SR)) / SR

def fft_noise(n, shape, seed=None):
    """Periodic noise of length n whose magnitude spectrum is shape(freq_hz)."""
    r = np.random.default_rng(seed) if seed is not None else rng
    f = np.fft.rfftfreq(n, 1 / SR)
    mag = shape(np.maximum(f, 1e-3)); mag[0] = 0
    ph = r.uniform(0, 2 * np.pi, len(f))
    x = np.fft.irfft(mag * np.exp(1j * ph), n)
    return x / (np.max(np.abs(x)) + 1e-9)

def band(lo, hi, slope=1.0):
    def s(f):
        m = 1 / (f ** (0.5 * slope))
        m *= 1 / (1 + (lo / f) ** 4)
        m *= 1 / (1 + (f / hi) ** 4)
        return m
    return s

def periodic_lfo(n, cycles, phase=0.0):
    return np.sin(2 * np.pi * cycles * np.arange(n) / n + phase)

def smooth_env(n, k_list, seed=None):
    """Sum of periodic sines with integer cycle counts → seamless slow modulation in [0,1]."""
    r = np.random.default_rng(seed)
    e = np.zeros(n)
    for k in k_list: e += r.uniform(0.4, 1.0) * periodic_lfo(n, k, r.uniform(0, 6.28))
    e -= e.min(); e /= e.max() + 1e-9
    return e

def place(buf, ev, pos, wrap=True):
    n = len(buf); m = len(ev)
    if buf.ndim == 1:
        if wrap:
            idx = (pos + np.arange(m)) % n; np.add.at(buf, idx, ev)
        else:
            end = min(n, pos + m); buf[pos:end] += ev[:end - pos]
    else:
        for c in range(buf.shape[1]): place(buf[:, c], ev[:, c] if ev.ndim == 2 else ev, pos, wrap)

def pan(x, p):
    """Equal-power pan, p in [-1,1]."""
    a = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], -1)

def lp(x, fc, order=2): return signal.sosfilt(signal.butter(order, fc / (SR / 2), 'low', output='sos'), x, axis=0)
def hp(x, fc, order=2): return signal.sosfilt(signal.butter(order, fc / (SR / 2), 'high', output='sos'), x, axis=0)
def bp(x, lo, hi, order=2): return signal.sosfilt(signal.butter(order, [lo / (SR / 2), hi / (SR / 2)], 'band', output='sos'), x, axis=0)

def circ(fn, x, *a):
    """Apply a causal filter to a loop without a seam: filter three copies and keep the middle one."""
    n = len(x)
    y = fn(np.concatenate([x, x, x], 0), *a)
    return y[n:2 * n]

def qf(f, sec):
    """Round a frequency so it completes an integer number of cycles in the loop."""
    return max(1.0, round(f * sec)) / sec

def adsr(n, a, d, s, r_, sus_level=0.7):
    a, d, r_ = int(a * SR), int(d * SR), int(r_ * SR)
    e = np.full(n, sus_level)
    a = min(a, n); e[:a] = np.linspace(0, 1, a, endpoint=False) if a > 0 else e[:a]
    d = min(d, n - a); e[a:a + d] = np.linspace(1, sus_level, d, endpoint=False)
    r_ = min(r_, n); e[n - r_:] *= np.linspace(1, 0, r_)
    return e

def reverb_ir(sec=2.4, damp=3000, seed=1, stereo=True, predelay=0.012):
    n = int(sec * SR)
    t = np.arange(n) / SR
    chans = []
    for c in range(2 if stereo else 1):
        r = np.random.default_rng(seed + c)
        x = r.standard_normal(n) * np.exp(-t * 6.9 / sec)
        x = lp(x, damp, 1)
        x = np.concatenate([np.zeros(int(predelay * SR)), x])
        chans.append(x / np.sqrt(np.sum(x ** 2)))
    return np.stack(chans, -1) if stereo else chans[0]

def convolve_reverb(x, ir, wet=0.25, circular=False):
    """x: (n,2) or (n,). Circular convolution keeps loops seamless."""
    if x.ndim == 1: x = np.stack([x, x], -1)
    n = len(x); out = np.zeros_like(x)
    for c in range(2):
        h = ir[:, c] if ir.ndim == 2 else ir
        if circular:
            L = n
            hh = np.zeros(L); m = min(len(h), L); hh[:m] = h[:m]
            y = np.fft.irfft(np.fft.rfft(x[:, c]) * np.fft.rfft(hh), L)
        else:
            y = signal.fftconvolve(x[:, c], h)[:n]
        out[:, c] = y
    return x * (1 - wet) + out * wet * 1.6

def normalize(x, peak=0.89):
    m = np.max(np.abs(x)) + 1e-9
    return x * (peak / m)

def write(name, x, quality=5, peak=0.89, fade=True):
    x = np.asarray(x, np.float64)
    if fade and len(x) > 2000:  # tiny anti-click edges for one-shots
        k = 64; w = np.linspace(0, 1, k)
        if x.ndim == 1: x[:k] *= w; x[-k:] *= w[::-1]
        else: x[:k] *= w[:, None]; x[-k:] *= w[::-1, None]
    x = normalize(x, peak)
    ch = 1 if x.ndim == 1 else x.shape[1]
    pcm = (np.clip(x, -1, 1) * 32767).astype('<i2').tobytes()
    path = os.path.join(OUT, name + '.ogg')
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-f', 's16le', '-ar', str(SR), '-ac', str(ch), '-i', '-',
                    '-c:a', 'libvorbis', '-q:a', str(quality), path], input=pcm, check=True)
    print('wrote', name, f'{len(x) / SR:.1f}s', ch, 'ch')

# ------------------------------------------------------------------ instruments
def bell(freq, sec, bright=1.0, decay=1.6):
    t = t_axis(sec)
    partials = [(1.0, 1.0, 1.0), (2.0, 0.45 * bright, 1.6), (2.76, 0.32 * bright, 2.3), (5.40, 0.16 * bright, 3.5), (8.93, 0.06 * bright, 5.0)]
    x = np.zeros_like(t)
    for ratio, amp, dk in partials:
        x += amp * np.sin(2 * np.pi * freq * ratio * t + rng.uniform(0, 6.28)) * np.exp(-t * dk * decay)
    x *= np.minimum(1, t / 0.003)
    return x

def marimba(freq, sec):
    t = t_axis(sec)
    x = np.sin(2 * np.pi * freq * t) * np.exp(-t * 5) + 0.25 * np.sin(2 * np.pi * freq * 4 * t) * np.exp(-t * 14) + 0.08 * np.sin(2 * np.pi * freq * 10 * t) * np.exp(-t * 40)
    return x * np.minimum(1, t / 0.002)

def pluck(freq, sec, bright=0.5, seed=None):
    """Karplus-Strong plucked string (harp/guitar-ish)."""
    n = int(sec * SR); p = max(2, int(round(SR / freq)))
    r = np.random.default_rng(seed)
    buf = lp(r.uniform(-1, 1, p * 4), 1500 + 6000 * bright, 1)[-p:]
    out = np.zeros(n)
    decay = 0.996 - 0.002 * (freq / 1000)
    b = buf.copy(); i = 0
    # vectorised in blocks of p samples
    pos = 0
    while pos < n:
        m = min(p, n - pos)
        out[pos:pos + m] = b[:m]
        nb = decay * 0.5 * (b + np.roll(b, -1))
        b = nb; pos += p
    out = lp(out, 7000, 1)
    t = np.arange(n) / SR
    return out * np.exp(-t * 0.8)

def epiano(freq, sec, vel=1.0):
    t = t_axis(sec)
    mod = 1.2 * np.exp(-t * 3)
    x = np.sin(2 * np.pi * freq * t + mod * np.sin(2 * np.pi * freq * t))
    x += 0.3 * np.sin(2 * np.pi * freq * 2 * t) * np.exp(-t * 2)
    env = np.exp(-t * (1.1 + freq / 900)) * np.minimum(1, t / 0.004)
    return x * env * vel

def pad(freqs, sec, attack=2.5, release=3.0, bright=0.5, seed=0):
    t = t_axis(sec); n = len(t)
    r = np.random.default_rng(seed)
    L = np.zeros(n); R = np.zeros(n)
    for f in freqs:
        for det in (-0.13, 0.0, 0.11):
            ff = f * 2 ** (det / 12)
            ph = r.uniform(0, 6.28)
            vib = 1 + 0.0018 * np.sin(2 * np.pi * r.uniform(0.15, 0.3) * t)
            w = np.sin(2 * np.pi * ff * vib * t + ph) + bright * 0.35 * np.sin(4 * np.pi * ff * t + ph * 1.3) + bright * 0.12 * np.sin(6 * np.pi * ff * t)
            p = r.uniform(-0.7, 0.7)
            L += w * np.cos((p + 1) * np.pi / 4); R += w * np.sin((p + 1) * np.pi / 4)
    env = adsr(n, attack, 0.5, 1.0, release, 1.0)
    return np.stack([L * env, R * env], -1) / (len(freqs) * 3)

def flute(freq, sec, vel=0.8, seed=0):
    t = t_axis(sec); n = len(t)
    r = np.random.default_rng(seed)
    vib = 1 + 0.006 * np.sin(2 * np.pi * 5.0 * t) * np.minimum(1, t / 0.6)
    ph = 2 * np.pi * np.cumsum(freq * vib) / SR
    x = np.sin(ph) + 0.22 * np.sin(2 * ph) + 0.06 * np.sin(3 * ph)
    breath = bp(r.standard_normal(n), freq * 0.9, min(freq * 3.5, 15000), 2) * 0.10
    env = adsr(n, 0.12, 0.2, 0.85, min(0.35, sec * 0.4), 0.85)
    return (x + breath) * env * vel

def midi(m): return 440.0 * 2 ** ((m - 69) / 12)

# ------------------------------------------------------------------ ambience loops
def amb_wind(sec=48):
    n = int(sec * SR)
    out = np.zeros((n, 2))
    for c in range(2):
        low = fft_noise(n, band(60, 700, 2.0), seed=11 + c)
        mid = fft_noise(n, band(300, 2200, 1.2), seed=21 + c)
        hiss = fft_noise(n, band(1800, 9000, 0.6), seed=31 + c)
        e1 = 0.35 + 0.65 * smooth_env(n, [1, 2, 3, 5], seed=41)
        e2 = smooth_env(n, [2, 3, 7, 11], seed=51 + c) ** 2
        out[:, c] = low * e1 * 0.9 + mid * e2 * 0.45 + hiss * e2 * 0.10
    write('amb_wind', out, fade=False, peak=0.6)

def bird_whistle(r):
    seg = []
    f0 = r.uniform(2600, 4200); notes = r.integers(2, 6)
    for k in range(notes):
        d = r.uniform(0.07, 0.16); t = t_axis(d)
        sweep = f0 * (1 + r.uniform(-0.35, 0.15) * t / d)
        ph = 2 * np.pi * np.cumsum(sweep) / SR
        env = np.sin(np.pi * t / d) ** 1.5
        seg.append(np.sin(ph) * env); seg.append(np.zeros(int(r.uniform(0.03, 0.09) * SR)))
        f0 *= r.uniform(0.9, 1.12)
    return np.concatenate(seg)

def bird_trill(r):
    d = r.uniform(0.5, 1.2); t = t_axis(d)
    f = r.uniform(3800, 5600) * (1 - 0.15 * t / d)
    am = 0.5 + 0.5 * np.sin(2 * np.pi * r.uniform(18, 32) * t)
    ph = 2 * np.pi * np.cumsum(f * (1 + 0.04 * np.sin(2 * np.pi * 30 * t))) / SR
    return np.sin(ph) * am * np.sin(np.pi * t / d) ** 0.7

def bird_dove(r):
    seg = []
    f = r.uniform(420, 560)
    for d, g in [(0.35, 0.18), (0.55, 0.12), (0.3, 0.6)]:
        t = t_axis(d)
        ph = 2 * np.pi * np.cumsum(f * (1 + 0.06 * np.sin(np.pi * t / d))) / SR
        seg.append((np.sin(ph) + 0.2 * np.sin(2 * ph)) * np.sin(np.pi * t / d) ** 2); seg.append(np.zeros(int(g * SR)))
    return np.concatenate(seg)

def amb_day(sec=64):
    n = int(sec * SR)
    out = np.zeros((n, 2))
    for c in range(2):
        out[:, c] = fft_noise(n, band(80, 1200, 1.8), seed=61 + c) * (0.3 + 0.2 * smooth_env(n, [1, 3, 4], seed=62)) * 0.22
        out[:, c] += fft_noise(n, band(2500, 9000, 0.5), seed=63 + c) * 0.012  # distant leaves
    r = np.random.default_rng(7)
    # a few "individual" birds, each with its own position and calling rhythm
    birds = [(bird_whistle, r.uniform(-0.8, 0.8), r.uniform(0.25, 0.6), r.uniform(3.5, 7)) for _ in range(4)]
    birds += [(bird_trill, r.uniform(-0.9, 0.9), r.uniform(0.15, 0.35), r.uniform(6, 12)) for _ in range(2)]
    birds += [(bird_dove, r.uniform(-0.6, 0.6), 0.22, 16.0)]
    for fn, p, vol, every in birds:
        tpos = r.uniform(0, every)
        while tpos < sec:
            ev = fn(r) * vol * r.uniform(0.6, 1.0)
            if fn is not bird_dove: ev = hp(ev, 1500, 2)
            dist = r.uniform(0, 1)
            ev = lp(ev, 9000 - 5000 * dist, 1) * (1 - 0.6 * dist)
            place(out, pan(ev, np.clip(p + r.uniform(-0.1, 0.1), -1, 1)), int(tpos * SR))
            tpos += every * r.uniform(0.6, 1.6)
    out = convolve_reverb(out, reverb_ir(1.6, 5000, seed=3), wet=0.22, circular=True)
    write('amb_day', out, fade=False, peak=0.55)

def cricket(r, sec, rate, freq):
    n = int(sec * SR); t = np.arange(n) / SR
    carrier = np.sin(2 * np.pi * qf(freq, sec) * t)
    pulses = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * qf(r.uniform(28, 40), sec) * t))
    chirp_gate = (np.sin(2 * np.pi * rate * t + r.uniform(0, 6.28)) > r.uniform(0.2, 0.5)).astype(float)
    chirp_gate = circ(lp, chirp_gate, 60, 1)
    pulses = circ(lp, pulses, 400, 1)
    breathe = 0.6 + 0.4 * smooth_env(n, [1, 2, 3], seed=int(freq))
    return carrier * pulses * chirp_gate * breathe

def owl(r):
    seg = []
    f = r.uniform(330, 400)
    for d, g in [(0.28, 0.25), (0.7, 0.0)]:
        t = t_axis(d)
        ph = 2 * np.pi * np.cumsum(f * (1 - 0.05 * t / d + 0.01 * np.sin(2 * np.pi * 7 * t))) / SR
        seg.append((np.sin(ph) + 0.15 * np.sin(2 * ph)) * np.sin(np.pi * t / d) ** 1.4); seg.append(np.zeros(int(g * SR) + 1))
    return np.concatenate(seg)

def amb_night(sec=64):
    n = int(sec * SR)
    out = np.zeros((n, 2))
    for c in range(2):
        out[:, c] = fft_noise(n, band(60, 700, 2.0), seed=71 + c) * (0.25 + 0.2 * smooth_env(n, [1, 2, 5], seed=72)) * 0.25
    r = np.random.default_rng(9)
    for k in range(7):
        # rates are integer multiples of 1/sec so every cricket's pattern loops exactly
        rate = round(r.uniform(1.4, 3.2) * sec) / sec
        cr = cricket(r, sec, rate, r.uniform(3900, 5200)) * r.uniform(0.05, 0.16)
        cr = circ(lp, cr, r.uniform(5000, 9000), 1)
        out += pan(cr, r.uniform(-0.9, 0.9))
    for k in range(2):
        tpos = r.uniform(5, sec - 5)
        place(out, pan(lp(owl(r), 2500, 1) * 0.35, r.uniform(-0.6, 0.6)), int(tpos * SR))
    out = convolve_reverb(out, reverb_ir(2.2, 4000, seed=5), wet=0.3, circular=True)
    write('amb_night', out, fade=False, peak=0.5)

def amb_river(sec=36):
    n = int(sec * SR)
    out = fft_noise(n, band(250, 5500, 0.9), seed=81) * 0.55
    out += fft_noise(n, band(80, 400, 1.5), seed=82) * 0.25
    out *= 0.85 + 0.15 * smooth_env(n, [3, 5, 8, 13], seed=83)
    r = np.random.default_rng(11)
    for k in range(int(sec * 22)):
        d = r.uniform(0.012, 0.05); t = t_axis(d)
        f0 = r.uniform(500, 2400)
        ph = 2 * np.pi * np.cumsum(f0 * (1 + r.uniform(0.3, 1.4) * t / d)) / SR
        ev = np.sin(ph) * np.exp(-t / (d * 0.35)) * r.uniform(0.03, 0.14)
        place(out, ev, int(r.uniform(0, n)))
    out = circ(bp, out, 120, 9000, 1)
    write('amb_river', out, fade=False, peak=0.6)

def amb_fire(sec=30):
    n = int(sec * SR)
    out = fft_noise(n, band(40, 260, 2.0), seed=91) * (0.55 + 0.45 * smooth_env(n, [3, 7, 11, 17], seed=92)) * 0.5
    out += fft_noise(n, band(900, 6000, 0.8), seed=93) * 0.03
    r = np.random.default_rng(13)
    for k in range(int(sec * 28)):
        d = r.uniform(0.002, 0.012); m = int(d * SR)
        ev = r.standard_normal(m) * np.exp(-np.linspace(0, 6, m))
        ev = bp(ev, r.uniform(1200, 3000), r.uniform(4000, 11000), 1) * r.uniform(0.05, 0.35) ** 1.4
        place(out, ev, int(r.uniform(0, n)))
    for k in range(int(sec * 1.2)):  # occasional pops
        m = int(0.03 * SR); ev = r.standard_normal(m) * np.exp(-np.linspace(0, 9, m))
        place(out, bp(ev, 400, 5000, 1) * r.uniform(0.3, 0.8), int(r.uniform(0, n)))
    write('amb_fire', out, fade=False, peak=0.6)

def terrain_rumble(sec=6):
    n = int(sec * SR)
    out = fft_noise(n, band(25, 180, 2.2), seed=101) * (0.7 + 0.3 * smooth_env(n, [2, 5, 9], seed=102))
    r = np.random.default_rng(15)
    grit = np.zeros(n)
    for k in range(int(sec * 160)):
        m = int(r.uniform(0.002, 0.008) * SR); ev = r.standard_normal(m) * np.exp(-np.linspace(0, 5, m))
        place(grit, ev * r.uniform(0.05, 0.3), int(r.uniform(0, n)))
    out += circ(bp, grit, 500, 3500, 1) * 0.6
    write('terrain_rumble', out, fade=False, peak=0.7)

# ------------------------------------------------------------------ one-shots
def tail(x, sec): return np.concatenate([x, np.zeros((int(sec * SR),) + x.shape[1:])])

def chord_bells(notes, gap, sec, bright=0.8, decay=1.4, spread=0.5, wet=0.35, gains=None):
    n = int(sec * SR); out = np.zeros((n, 2))
    for i, m in enumerate(notes):
        g = gains[i] if gains else 1.0
        ev = bell(midi(m), sec - i * gap, bright, decay) * g
        place(out, pan(ev, np.clip((i / max(1, len(notes) - 1) - 0.5) * 2 * spread, -1, 1)), int(i * gap * SR), wrap=False)
    return convolve_reverb(out, reverb_ir(2.0, 6000, seed=21), wet=wet)

def milestone():
    out = chord_bells([67, 72, 76, 79, 84], 0.11, 3.2, 0.7, 1.0, 0.7, 0.4, [0.9, 0.9, 0.85, 0.8, 0.55])
    p = pad([midi(48), midi(55), midi(64)], 3.2, 0.05, 2.2, 0.3, seed=5) * 1.6
    write('milestone', out + p)

def complete_chime():
    write('complete_chime', chord_bells([76, 81], 0.14, 2.0, 0.6, 1.6, 0.4, 0.3))

def summon():
    sec = 2.6; n = int(sec * SR)
    out = chord_bells([72, 74, 79, 81, 86, 88, 91], 0.075, sec, 1.0, 1.8, 0.9, 0.45, [0.5, 0.55, 0.6, 0.65, 0.6, 0.55, 0.5])
    sw = fft_noise(n, band(1500, 12000, 0.4), seed=111)
    env = np.sin(np.pi * np.minimum(1, np.arange(n) / (0.9 * SR))) ** 2 * np.exp(-np.arange(n) / SR * 1.2)
    out += np.stack([sw * env, np.roll(sw, 999) * env], -1) * 0.12
    write('summon', out)

def whoosh(sec, lo, hi, seed, peak_at=0.35):
    n = int(sec * SR); t = np.arange(n) / SR
    x = np.random.default_rng(seed).standard_normal(n)
    # time-varying band-pass: crossfade between filtered copies
    a = bp(x, lo, lo * 3, 2); b = bp(x, hi / 3, hi, 2)
    k = np.clip(t / (sec * peak_at), 0, 1)
    env = np.minimum(1, t / (sec * peak_at)) ** 1.5 * np.exp(-np.maximum(0, t - sec * peak_at) * 6 / sec)
    return (a * (1 - k) + b * k) * env

def poof():
    x = whoosh(0.8, 300, 3000, 121, 0.18)
    thump = np.sin(2 * np.pi * 90 * t_axis(0.8)) * np.exp(-t_axis(0.8) * 18) * 0.5
    write('poof', convolve_reverb(x + thump, reverb_ir(1.0, 5000, seed=23), wet=0.25))

def water_body(sec, density, seed, lo=300, hi=6000):
    n = int(sec * SR); r = np.random.default_rng(seed)
    x = bp(r.standard_normal(n), lo, hi, 2) * 0.4
    for k in range(int(sec * density)):
        d = r.uniform(0.01, 0.05); t = t_axis(d)
        f0 = r.uniform(400, 2200)
        ph = 2 * np.pi * np.cumsum(f0 * (1 + r.uniform(0.4, 1.5) * t / d)) / SR
        place(x, np.sin(ph) * np.exp(-t / (d * 0.35)) * r.uniform(0.1, 0.4), int(r.uniform(0, n)), wrap=False)
    return x

def river_create():
    sec = 3.2; n = int(sec * SR); t = np.arange(n) / SR
    x = water_body(sec, 70, 131) * (np.minimum(1, t / 0.9) ** 2 * np.exp(-np.maximum(0, t - 1.4) * 1.6))
    rumble = lp(np.random.default_rng(132).standard_normal(n), 160, 2) * np.minimum(1, t / 0.4) * np.exp(-t * 1.2) * 1.6
    write('river_create', convolve_reverb(x + rumble, reverb_ir(1.4, 4000, seed=25), wet=0.25))

def water_rush():
    sec = 4.0; n = int(sec * SR); t = np.arange(n) / SR
    x = water_body(sec, 50, 141, 200, 4500) * (np.minimum(1, t / 1.5) ** 1.5 * np.exp(-np.maximum(0, t - 2.0) * 2.0))
    write('water_rush', convolve_reverb(x, reverb_ir(1.8, 3500, seed=27), wet=0.35))

def splash(name, sec, size, seed):
    n = int(sec * SR); t = np.arange(n) / SR; r = np.random.default_rng(seed)
    burst = bp(r.standard_normal(n), 400, 9000, 2) * np.exp(-t * (14 / size))
    body = water_body(sec, 90 * size, seed + 1) * np.exp(-t * (5 / size)) * 0.8
    drop = np.sin(2 * np.pi * np.cumsum(220 * size ** -0.3 * (1 + 2 * t)) / SR) * np.exp(-t * 20) * 0.4
    write(name, convolve_reverb(burst + body + drop, reverb_ir(0.9, 6000, seed=29), wet=0.18))

def water_scoop():
    sec = 1.0; n = int(sec * SR); t = np.arange(n) / SR; r = np.random.default_rng(151)
    x = np.zeros(n)
    for k, st in enumerate(np.linspace(0.05, 0.45, 6)):
        d = 0.09; tt = t_axis(d); f0 = 900 - 90 * k
        ph = 2 * np.pi * np.cumsum(f0 * (1 + 1.2 * tt / d)) / SR
        place(x, np.sin(ph) * np.exp(-tt / 0.03) * 0.5, int(st * SR), wrap=False)
    x += bp(r.standard_normal(n), 500, 7000, 2) * np.exp(-((t - 0.1) / 0.12) ** 2) * 0.4
    write('water_scoop', x)

def warning():
    sec = 1.6; n = int(sec * SR); out = np.zeros(n)
    place(out, marimba(midi(69), 1.2), 0, wrap=False); place(out, marimba(midi(65), 1.2) * 0.9, int(0.22 * SR), wrap=False)
    place(out, marimba(midi(57), 1.2) * 0.5, 0, wrap=False)
    write('warning', convolve_reverb(out, reverb_ir(1.2, 5000, seed=31), wet=0.25))

def hint():
    sec = 1.4; n = int(sec * SR); out = np.zeros(n)
    place(out, bell(midi(81), 1.2, 0.4, 2.2) * 0.8, 0, wrap=False); place(out, bell(midi(88), 1.1, 0.4, 2.4) * 0.5, int(0.09 * SR), wrap=False)
    write('hint', convolve_reverb(out, reverb_ir(1.2, 6000, seed=33), wet=0.3))

def tree_fall(creak_paths, thud_paths):
    def load(p):
        raw = subprocess.run(['ffmpeg', '-loglevel', 'error', '-i', p, '-f', 's16le', '-ac', '1', '-ar', str(SR), '-'], capture_output=True, check=True).stdout
        return np.frombuffer(raw, '<i2').astype(np.float64) / 32768
    sec = 4.2; n = int(sec * SR); t = np.arange(n) / SR; r = np.random.default_rng(161)
    out = np.zeros(n)
    c = load(creak_paths[0]); place(out, c * 0.9, 0, wrap=False)
    if len(creak_paths) > 1: place(out, load(creak_paths[1]) * 0.7, int(0.55 * SR), wrap=False)
    # splintering crack
    crack = hp(r.standard_normal(int(0.25 * SR)), 1200, 2) * np.exp(-np.linspace(0, 8, int(0.25 * SR)))
    place(out, crack * 0.8, int(0.3 * SR), wrap=False)
    # rushing leaves while it falls
    leaves = bp(r.standard_normal(n), 1500, 9000, 2) * np.exp(-((t - 1.6) / 0.55) ** 2) * 0.35
    out += leaves
    # impact
    hit = int(2.05 * SR)
    th = lp(r.standard_normal(n - hit), 220, 2) * np.exp(-np.arange(n - hit) / SR * 4.5) * 2.4
    th += np.sin(2 * np.pi * 52 * np.arange(n - hit) / SR) * np.exp(-np.arange(n - hit) / SR * 6) * 0.8
    out[hit:] += th
    for p in thud_paths: place(out, load(p) * 0.8, hit + int(r.uniform(0, 0.05) * SR), wrap=False)
    out[hit:] += bp(r.standard_normal(n - hit), 2000, 8000, 2) * np.exp(-np.arange(n - hit) / SR * 3.0) * 0.2
    write('tree_fall', convolve_reverb(out, reverb_ir(1.6, 4000, seed=35), wet=0.2))

def forest_plant():
    """God power: a swelling rustle of growing leaves, a soft earthy thump and a faint magical sparkle."""
    sec = 2.6; n = int(sec * SR); t = np.arange(n) / SR; r = np.random.default_rng(191)
    rustle = np.zeros(n)
    for k in range(260):
        m = int(r.uniform(0.01, 0.05) * SR); ev = r.standard_normal(m) * np.hanning(m)
        when = r.uniform(0, 1.6) ** 1.3
        place(rustle, bp(ev, r.uniform(1500, 3000), r.uniform(6000, 11000), 1) * r.uniform(0.1, 0.5), int(when * SR), wrap=False)
    rustle *= np.minimum(1, t / 0.5) * np.exp(-np.maximum(0, t - 1.2) * 2.5)
    thump = lp(r.standard_normal(n), 180, 2) * np.exp(-t * 7) * 1.2 + np.sin(2 * np.pi * 70 * t) * np.exp(-t * 9) * 0.5
    out = pan(rustle, -0.2) + pan(np.roll(rustle, 777), 0.2) + pan(thump, 0)
    out += chord_bells([79, 84, 88], 0.12, sec, 0.6, 2.2, 0.6, 0.4, [0.12, 0.10, 0.08])
    write('forest_plant', convolve_reverb(out, reverb_ir(1.3, 6000, seed=37), wet=0.2))

# ------------------------------------------------------------------ music
class Song:
    def __init__(self, bpm, bars, beats=4):
        self.spb = 60 / bpm; self.beats = beats; self.bars = bars
        self.sec = bars * beats * self.spb + 8
        self.n = int(self.sec * SR)
        self.buf = np.zeros((self.n, 2))
    def at(self, bar, beat=0.0): return (bar * self.beats + beat) * self.spb
    def add(self, x, when, p=0.0, gain=1.0):
        if x.ndim == 1: x = pan(x, p)
        place(self.buf, x * gain, int(when * SR), wrap=False)

def make_melody(r, scale, bars, beats, motif_len=2, density=0.55, low=0, high=None):
    """Motif-based generative melody: a 2-bar motif repeated with variation, stepwise motion preferred."""
    high = high if high is not None else len(scale) - 1
    def gen_motif():
        notes = []; pos = 0.0; idx = r.integers(low + 2, high - 1)
        while pos < motif_len * beats - 0.01:
            dur = r.choice([0.5, 1.0, 1.0, 1.5, 2.0], p=[0.15, 0.35, 0.2, 0.15, 0.15])
            dur = min(dur, motif_len * beats - pos)
            if r.uniform() < density: notes.append((pos, dur, int(idx)))
            pos += dur
            idx = int(np.clip(idx + r.choice([-2, -1, -1, 0, 1, 1, 2, 3]), low, high))
        return notes
    a, b = gen_motif(), gen_motif()
    form = [a, b, a, None, a, b, b, None]
    out = []
    for i in range(bars // motif_len):
        m = form[i % len(form)]
        if m is None: continue
        shift = 0 if r.uniform() < 0.7 else int(r.choice([-1, 1, 2]))
        for pos, dur, idx in m:
            out.append((i * motif_len * beats + pos, dur, int(np.clip(idx + shift, low, high))))
    return out

def song(name, bpm, key_root, mode, progression, bars, seed, lead='flute', bells=True, harp=True, wet=0.42, lead_gain=0.32):
    r = np.random.default_rng(seed)
    s = Song(bpm, bars)
    steps = {'major': [0, 2, 4, 7, 9], 'minor': [0, 3, 5, 7, 10], 'lydian': [0, 2, 4, 7, 9]}[mode]
    scale = [key_root + 12 * o + st for o in (0, 1, 2) for st in steps]
    bar_sec = s.beats * s.spb
    # chords: (root offset from key, quality)
    for i in range(bars):
        rt, q = progression[i % len(progression)]
        root = key_root - 12 + rt
        third = 3 if q == 'm' else 4
        notes = [root, root + 7, root + 12 + third, root + 19] + ([root + 14] if r.uniform() < 0.4 else [])
        if i % 2 == 0 or len(progression) < 4 or True:
            s.add(pad([midi(m) for m in notes], bar_sec + 2.2, 1.6, 2.4, 0.4, seed=seed + i), s.at(i), gain=0.55)
        # bass: soft sine on the root, beat 1 (and 3 sometimes)
        bt = t_axis(bar_sec * 0.95)
        bass = (np.sin(2 * np.pi * midi(root - 12) * bt) + 0.2 * np.sin(4 * np.pi * midi(root - 12) * bt)) * adsr(len(bt), 0.05, 0.4, 0.6, 0.8, 0.6)
        s.add(bass, s.at(i), 0.0, 0.22)
        if harp and i >= 2:
            # arpeggiated plucks through the chord
            arp = [root + 12, root + 19, root + 12 + third + 12, root + 24, root + 19 + 12 if r.uniform() < 0.5 else root + 24 + third]
            pattern = r.choice([[0, 1, 2, 3, 2, 1, 0, 1], [0, 2, 1, 3, 0, 2, 1, 4], [0, 1, 2, 1, 3, 2, 4, 2]])
            for k, pi in enumerate(pattern):
                if r.uniform() < 0.82:
                    s.add(pluck(midi(arp[pi % len(arp)]), 2.5, 0.45, seed=seed * 100 + i * 10 + k), s.at(i, k * 0.5), r.uniform(-0.5, 0.5), 0.20 * r.uniform(0.75, 1.0))
    # melody enters after an intro and leaves before the outro
    mel = make_melody(r, scale, bars - 8, s.beats, density=0.62, low=3, high=len(scale) - 3)
    for pos, dur, idx in mel:
        when = s.at(4) + pos * s.spb
        f = midi(scale[idx] + 12)
        if lead == 'flute': x = flute(f, dur * s.spb + 0.25, 0.8, seed=int(pos * 10))
        else: x = epiano(f, dur * s.spb + 1.5, 0.8)
        s.add(x, when, 0.12, lead_gain)
    if bells:
        for i in range(4, bars - 2, 4):
            m = scale[int(r.integers(5, len(scale) - 1))] + 12
            s.add(bell(midi(m), 4.0, 0.5, 1.2), s.at(i, 0.0), r.uniform(-0.6, 0.6), 0.10)
    out = convolve_reverb(s.buf, reverb_ir(3.2, 5000, seed=seed), wet=wet)
    # gentle master: fade in/out
    n = len(out); t = np.arange(n) / SR
    out *= np.minimum(1, t / 3.0)[:, None] * np.minimum(1, (s.sec - t) / 7.0)[:, None]
    write(name, out, quality=5, peak=0.7)

def music():
    I, ii, iii, IV, V, vi = (0, ''), (2, 'm'), (4, 'm'), (5, ''), (7, ''), (9, 'm')
    song('music_morning', 76, 62, 'major', [I, vi, IV, V, I, iii, IV, V], 40, 301, 'flute')
    song('music_river', 68, 65, 'lydian', [I, (7, ''), (2, 'm'), (-2 + 12, '')], 36, 302, 'epiano', lead_gain=0.28)
    i_, VI, III, VII, iv = (0, 'm'), (8, ''), (3, ''), (10, ''), (5, 'm')
    song('music_dusk', 60, 57, 'minor', [i_, VI, III, VII, i_, iv, VI, VII], 32, 303, 'epiano', harp=True, wet=0.5, lead_gain=0.24)

if __name__ == '__main__':
    which = set(sys.argv[2:])
    jobs = {
        'amb': lambda: (amb_wind(), amb_day(), amb_night(), amb_river(), amb_fire(), terrain_rumble()),
        'sfx': lambda: (milestone(), complete_chime(), summon(), poof(), river_create(), water_rush(),
                        forest_plant(), splash('splash', 1.4, 1.0, 171), splash('splash_small', 0.8, 0.5, 181), water_scoop(), warning(), hint(),
                        tree_fall(['/data/dl/kenney/kenney_rpg-audio/Audio/creak1.ogg', '/data/dl/kenney/kenney_rpg-audio/Audio/creak3.ogg'],
                                  ['/data/dl/kenney/kenney_impact-sounds/Audio/impactWood_heavy_001.ogg', '/data/dl/kenney/kenney_impact-sounds/Audio/impactSoft_heavy_002.ogg'])),
        'music': music,
    }
    for k, f in jobs.items():
        if not which or k in which: f()
