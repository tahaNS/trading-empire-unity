#!/usr/bin/env python3
"""Builds Trading-Empire-Unity-trailer.mp4 from the screenshots in docs/screenshots.

Usage: python3 make_trailer.py /path/to/ffmpeg
Every scene is a screenshot with a slow zoom, a caption bar and fades; a soft synthesised pad plays underneath.
"""
import os, subprocess, sys, tempfile

FFMPEG = sys.argv[1] if len(sys.argv) > 1 else "ffmpeg"
HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.join(HERE, "..", "screenshots")
OUT = os.path.join(HERE, "Trading-Empire-Unity-trailer.mp4")
FONT = "/System/Library/Fonts/Helvetica.ttc"
D = 3.8  # seconds per scene

# (image, headline, sub line)
SCENES = [
    ("city", "A living city that pays rent", "Shops, apartments, offices, towers, parks and hotels. Build, upgrade, grow."),
    ("building-card", "Every building shows what it earns", "Income, upkeep, neighbour bonuses and the next upgrade, at a glance."),
    ("terminal", "A manual trading terminal", "Replay real market history candle by candle. No peeking at the future."),
    ("order-plan", "Plan every trade on the chart", "Drag entry, stop-loss and take-profit. Size by risk, or by lots. Market, limit or stop."),
    ("indicators", "16 indicators", "Moving averages, Bollinger Bands, RSI, MACD, Stochastic, ADX and more."),
    ("drawing-tools", "21 drawing tools", "Trend lines, channels, pitchforks, Gann fans, position boxes and Fibonacci tools."),
    ("fibonacci-levels", "Edit every Fibonacci level", "Change a number, add a level, delete one, colour it, shade between levels."),
    ("load-data", "Bring your own market data", "Load MetaTrader 4 and 5 history, or a plain CSV, for forex pairs and gold."),
    ("terminal-guide", "A guide built in", "How to open a position, the drawing tools, loading data and every shortcut."),
    ("journal", "Keep a trade journal", "Notes, tags, a star rating, and a small chart of every trade with entry and exit."),
    ("statistics", "Know your numbers", "Win rate, profit factor, average R, drawdown and a table of every trade."),
    ("finances", "Profit builds the city", "Move realised profit from your trading account into the city treasury."),
    ("city", "Your trading shapes the city", "Hot streaks start a boom, cold ones a panic. Fires, storms and festivals strike."),
    ("challenges", "Trading challenges", "Practice runs on made-up markets. Win stars and unlock new city policies."),
    ("progress-panel", "Missions, land and achievements", "Complete missions to unlock bigger buildings and more land."),
    ("god-mode", "Built to be tested", "God mode, daily backups, saves that also open in the web version."),
]

tmp = tempfile.mkdtemp()

def run(args):
    subprocess.run([FFMPEG, "-y", "-loglevel", "error"] + args, check=True)

def write(name, text):
    path = os.path.join(tmp, name)
    with open(path, "w") as f:
        f.write(text)
    return path

def caption_filters(h, s, dur):
    t1, t2 = write("h%d.txt" % abs(hash(h)), h), write("s%d.txt" % abs(hash(s)), s)
    return (
        "drawbox=y=ih-220:w=iw:h=220:color=black@0.66:t=fill,"
        "drawbox=y=ih-220:w=12:h=220:color=0x2f7de1@1:t=fill,"
        "drawtext=fontfile=%s:textfile=%s:fontsize=60:fontcolor=white:borderw=1:bordercolor=white:x=84:y=h-186,"
        "drawtext=fontfile=%s:textfile=%s:fontsize=30:fontcolor=0xc9d4e3:x=84:y=h-100,"
        "fade=t=in:st=0:d=0.45,fade=t=out:st=%.2f:d=0.45" % (FONT, t1, FONT, t2, dur - 0.45)
    )

segments = []

# title card: the city, darkened and blurred, with big letters
title = os.path.join(tmp, "000.mp4")
t_main = write("tm.txt", "TRADING EMPIRE")
t_sub = write("ts.txt", "Trade the market. Build a city.")
run(["-framerate", "30", "-loop", "1", "-t", "4.6", "-i", os.path.join(SHOTS, "city.jpg"), "-vf",
     "scale=1920:1080,boxblur=18:2,eq=brightness=-0.28:saturation=1.1,"
     "drawtext=fontfile=%s:textfile=%s:fontsize=128:fontcolor=white:borderw=2:bordercolor=white:x=(w-text_w)/2:y=h/2-110,"
     "drawtext=fontfile=%s:textfile=%s:fontsize=44:fontcolor=0xdbe6f5:x=(w-text_w)/2:y=h/2+50,"
     "fade=t=in:st=0:d=0.8,fade=t=out:st=4.0:d=0.6,format=yuv420p" % (FONT, t_main, FONT, t_sub),
     "-c:v", "libx264", "-crf", "23", "-r", "30", title])
segments.append(title)

for i, (img, head, sub) in enumerate(SCENES, 1):
    out = os.path.join(tmp, "%03d.mp4" % i)
    vf = ("scale=3840:2160:flags=lanczos,zoompan=z='1+0.00055*on':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=1920x1080:fps=30,"
          + caption_filters(head, sub, D) + ",format=yuv420p")
    run(["-framerate", "30", "-loop", "1", "-t", str(D), "-i", os.path.join(SHOTS, img + ".jpg"), "-vf", vf,
         "-c:v", "libx264", "-crf", "25", "-r", "30", out])
    segments.append(out)

# end card
end = os.path.join(tmp, "999.mp4")
e1 = write("e1.txt", "TRADING EMPIRE")
e2 = write("e2.txt", "Made with Unity   github.com/tahaNS/trading-empire-unity")
run(["-framerate", "30", "-loop", "1", "-t", "4.6", "-i", os.path.join(SHOTS, "terminal.jpg"), "-vf",
     "scale=1920:1080,boxblur=18:2,eq=brightness=-0.3,"
     "drawtext=fontfile=%s:textfile=%s:fontsize=120:fontcolor=white:borderw=2:bordercolor=white:x=(w-text_w)/2:y=h/2-100,"
     "drawtext=fontfile=%s:textfile=%s:fontsize=38:fontcolor=0xdbe6f5:x=(w-text_w)/2:y=h/2+50,"
     "fade=t=in:st=0:d=0.6,fade=t=out:st=3.8:d=0.8,format=yuv420p" % (FONT, e1, FONT, e2),
     "-c:v", "libx264", "-crf", "23", "-r", "30", end])
segments.append(end)

lst = os.path.join(tmp, "list.txt")
with open(lst, "w") as f:
    for s in segments:
        f.write("file '%s'\n" % s)
silent = os.path.join(tmp, "silent.mp4")
run(["-f", "concat", "-safe", "0", "-i", lst, "-c", "copy", silent])

# a soft ambient pad (three sine voices with a slow swell), as long as the video
total = 4.6 + D * len(SCENES) + 4.6
pad = ("sine=f=110:d=%d,volume=0.5[a];sine=f=164.8:d=%d,volume=0.35[b];sine=f=220:d=%d,volume=0.3[c];sine=f=277.2:d=%d,volume=0.2[d];"
       "[a][b][c][d]amix=inputs=4:normalize=0,tremolo=f=0.18:d=0.5,lowpass=f=900,aecho=0.8:0.6:900:0.35,volume=0.5,afade=t=in:st=0:d=3,afade=t=out:st=%d:d=4[m]") % (
    total + 1, total + 1, total + 1, total + 1, total - 4)
run(["-i", silent, "-filter_complex", pad, "-map", "0:v", "-map", "[m]", "-c:v", "copy", "-c:a", "aac", "-b:a", "128k", "-shortest", OUT])
print("wrote", OUT, "%.0f seconds" % total)
