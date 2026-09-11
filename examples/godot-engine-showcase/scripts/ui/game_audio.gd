extends Node

var _music: AudioStreamPlayer
var _sfx: AudioStreamPlayer

func _ready() -> void:
	_music = AudioStreamPlayer.new()
	_sfx = AudioStreamPlayer.new()
	add_child(_music)
	add_child(_sfx)
	_music.stream = _tone(110.0, 4.0, 0.09, true, 164.81)
	_music.play()
	Preferences.changed.connect(_apply)
	_apply()

func ui() -> void:
	_play(540.0, 0.055, 0.16)

func card() -> void:
	_play(310.0, 0.12, 0.23)

func hit() -> void:
	_play(92.0, 0.16, 0.32)

func reward() -> void:
	_play(740.0, 0.22, 0.20)

func shutdown() -> void:
	if is_instance_valid(_music):
		_music.stop()
		_music.stream = null
	if is_instance_valid(_sfx):
		_sfx.stop()
		_sfx.stream = null

func _exit_tree() -> void:
	shutdown()

func _apply() -> void:
	_music.volume_db = linear_to_db(maxf(Preferences.music_volume, 0.001))
	_sfx.volume_db = linear_to_db(maxf(Preferences.sfx_volume, 0.001))

func _play(frequency: float, duration: float, amplitude: float) -> void:
	_sfx.stream = _tone(frequency, duration, amplitude)
	_sfx.play()

func _tone(frequency: float, duration: float, amplitude: float, looping := false, second := 0.0) -> AudioStreamWAV:
	var sample_rate := 22050
	var count := int(sample_rate * duration)
	var bytes := PackedByteArray()
	bytes.resize(count * 2)
	for i in count:
		var t := float(i) / sample_rate
		var envelope := 1.0 if looping else maxf(0.0, 1.0 - t / duration)
		var wave := sin(TAU * frequency * t)
		if second > 0.0:
			wave = (wave + sin(TAU * second * t) * 0.55) / 1.55
		var sample := int(clampf(wave * envelope * amplitude, -1.0, 1.0) * 32767.0)
		bytes.encode_s16(i * 2, sample)
	var stream := AudioStreamWAV.new()
	stream.format = AudioStreamWAV.FORMAT_16_BITS
	stream.mix_rate = sample_rate
	stream.stereo = false
	stream.data = bytes
	if looping:
		stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
		stream.loop_end = count
	return stream
