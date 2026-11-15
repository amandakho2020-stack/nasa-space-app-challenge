# Convert an .mp4 (or any video Blender can read) to Ogg Theora .ogv for Godot 4.
# Usage (PowerShell, from the project folder):
#   & "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" --background --factory-startup --python Tools/mp4_to_ogv.py -- "Art/Moon to Earth.mp4" "Art/Moon to Earth.ogv"
# Paths must be absolute or relative to the folder you run it from. Keeps size, frame rate and audio.
import bpy, sys
argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
scene = bpy.context.scene
scene.sequence_editor_create()
seqs = scene.sequence_editor.strips if hasattr(scene.sequence_editor, "strips") else scene.sequence_editor.sequences
movie = seqs.new_movie("video", src, 1, 1)
try:
    sound = seqs.new_sound("audio", src, 2, 1)
except Exception as e:
    sound = None
    print("[BLENDER] no audio:", e)
clip = bpy.data.movieclips.load(src)
w, h = clip.size
fps = clip.fps
print(f"[BLENDER] source {w}x{h} @ {fps} fps, {movie.frame_final_duration} frames, audio={sound is not None}")
scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = w, h, 100
scene.render.fps = int(round(fps)); scene.render.fps_base = scene.render.fps / fps if fps else 1
scene.frame_start, scene.frame_end = 1, movie.frame_final_duration
r = scene.render
if hasattr(r.image_settings, 'media_type'):
    r.image_settings.media_type = 'VIDEO'   # Blender 5.x: pick video output first
r.image_settings.file_format = 'FFMPEG'
r.ffmpeg.format = 'OGG'
r.ffmpeg.codec = 'THEORA'
r.ffmpeg.constant_rate_factor = 'HIGH'
r.ffmpeg.audio_codec = 'VORBIS' if sound else 'NONE'
r.ffmpeg.audio_bitrate = 192
r.filepath = dst
r.use_sequencer = True
bpy.ops.render.render(animation=True)
print("[BLENDER] wrote", dst)
