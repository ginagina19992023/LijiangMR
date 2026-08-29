#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
漓江回声 · 离线扒谱脚本(librosa HPSS / 频段起音检测)

用途:比 Unity 内置的全频能量检测更"干净"地扒出某类乐器的拍点,尤其是鼓点:
  用 HPSS(谐波-打击分离)把"打击成分"单独抽出来 → 对它做起音检测 = 很干净的鼓谱。
输出:与编辑器同格式的谱面文本(每行 "时间,类型",带 # types:explicit 头),
      可直接放到 Assets/Resources/LijiangEchoCharts/ 当 chart_levelN.txt,
      或在谱面编辑器里用「从文件导入拍点到新图层」导入成一个图层再编辑/合并。

安装:  pip install librosa numpy soundfile
用法示例:
  # 扒鼓点 → 存成关卡1(蛙纹关卡)的谱面
  python lijiang_beatmap.py song.mp3 --mode drums --out ../Assets/Resources/LijiangEchoCharts/chart_level0.txt
  # 扒中频管乐,最多 80 个最强拍
  python lijiang_beatmap.py song.mp3 --mode mid --top 80 --out horn.txt
  # 一次导出多层(鼓点+谐波+全频)到 out_dir,各一个文件,方便当不同图层导入
  python lijiang_beatmap.py song.mp3 --emit-layers --out-dir layers/

模式(--mode):
  full     整首混音(和 Unity 全频检测类似)
  drums    HPSS 打击成分(鼓点,推荐)
  harmonic HPSS 谐波成分(旋律/持续音)
  low      低频带通(约 20-150Hz,底鼓)
  mid      中频带通(约 300-2000Hz,管乐/唢呐等)
"""

import argparse
import os
import sys

import numpy as np

try:
    import librosa
except ImportError:
    sys.stderr.write("需要 librosa:pip install librosa numpy soundfile\n")
    sys.exit(1)


def bandlimit(y, sr, low_hz, high_hz):
    """用 STFT 频段掩码做带通(避免额外依赖 scipy)。"""
    stft = librosa.stft(y)
    freqs = librosa.fft_frequencies(sr=sr, n_fft=(stft.shape[0] - 1) * 2)
    mask = np.ones_like(freqs, dtype=bool)
    if low_hz > 0:
        mask &= freqs >= low_hz
    if high_hz > 0:
        mask &= freqs <= high_hz
    stft[~mask, :] = 0
    return librosa.istft(stft, length=len(y))


def component(y, sr, mode):
    """按模式取出要检测的信号成分。"""
    if mode == "full":
        return y
    if mode == "drums":
        _, perc = librosa.effects.hpss(y)
        return perc
    if mode == "harmonic":
        harm, _ = librosa.effects.hpss(y)
        return harm
    if mode == "low":
        return bandlimit(y, sr, 20, 150)
    if mode == "mid":
        return bandlimit(y, sr, 300, 2000)
    raise ValueError("未知模式:" + mode)


def detect(y, sr, delta, min_gap, top):
    """起音检测:返回 (时间[], 强度[]),已按最小间隔过滤、可取最强 N 个。"""
    env = librosa.onset.onset_strength(y=y, sr=sr)
    frames = librosa.onset.onset_detect(
        onset_envelope=env, sr=sr, backtrack=True,
        delta=delta, wait=int(max(0.0, min_gap) * sr / 512),
    )
    times = librosa.frames_to_time(frames, sr=sr)
    strengths = env[np.clip(frames, 0, len(env) - 1)]

    # 最小间隔(秒)去密
    kept_t, kept_s, last = [], [], -1e9
    for t, s in zip(times, strengths):
        if t - last >= min_gap:
            kept_t.append(float(t))
            kept_s.append(float(s))
            last = t
    times = np.array(kept_t)
    strengths = np.array(kept_s)

    # 取最强 N 个(再按时间排回)
    if top and top > 0 and len(times) > top:
        idx = np.argsort(strengths)[::-1][:top]
        idx = np.sort(idx)
        times = times[idx]
    return times


def write_chart(path, times, note_type):
    os.makedirs(os.path.dirname(os.path.abspath(path)) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write("# 漓江回声谱面(离线扒谱 lijiang_beatmap.py) —— 时间(秒),类型\n")
        f.write("# types:explicit  ← 运行时逐音符只认下面写的类型\n")
        for t in times:
            f.write("%.3f,%s\n" % (t, note_type))
    print("已写出 %d 个音符 → %s" % (len(times), path))


def main():
    ap = argparse.ArgumentParser(description="漓江回声离线扒谱(librosa)")
    ap.add_argument("audio", help="输入音频(mp3/wav/ogg…)")
    ap.add_argument("--mode", default="drums", choices=["full", "drums", "harmonic", "low", "mid"])
    ap.add_argument("--out", default="chart_out.txt", help="输出谱面文件")
    ap.add_argument("--type", default="single", help="音符类型 single/double/hold/swipe")
    ap.add_argument("--delta", type=float, default=0.07, help="峰值阈值(越大点越少)")
    ap.add_argument("--min-gap", type=float, default=0.12, help="两拍最小间隔(秒)")
    ap.add_argument("--top", type=int, default=0, help="只保留最强 N 个(0=全部)")
    ap.add_argument("--emit-layers", action="store_true", help="一次导出多层(drums/harmonic/full)各一个文件")
    ap.add_argument("--out-dir", default="layers", help="--emit-layers 的输出目录")
    args = ap.parse_args()

    print("加载音频:%s" % args.audio)
    y, sr = librosa.load(args.audio, sr=None, mono=True)
    print("采样率 %d,时长 %.1f 秒" % (sr, len(y) / sr))

    if args.emit_layers:
        for mode in ("drums", "harmonic", "full"):
            comp = component(y, sr, mode)
            times = detect(comp, sr, args.delta, args.min_gap, args.top)
            write_chart(os.path.join(args.out_dir, "chart_%s.txt" % mode), times, args.type)
        print("多层导出完成:在编辑器里用「从文件导入拍点到新图层」逐个导入,再按需合并。")
        return

    comp = component(y, sr, args.mode)
    times = detect(comp, sr, args.delta, args.min_gap, args.top)
    write_chart(args.out, times, args.type)


if __name__ == "__main__":
    main()
