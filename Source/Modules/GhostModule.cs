using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DebugModPlus.Modules;

public record GhostFrame(Vector3 Position, string SpriteName, int Facing, double Timestamp);

internal class GhostPlayback(SpriteRenderer playerCopy, GhostFrame[] frames, int playbackIndex) {
    public SpriteRenderer PlayerCopy = playerCopy;
    public GhostFrame[] Frames = frames;
    public int PlaybackIndex = playbackIndex;
}

public class GhostModule(ConfigEntry<Color> ghostColor) {
    private SpeedrunTimerModule speedrunTimerModule = DebugModPlus.Instance.SpeedrunTimerModule;

    // sprite cache
    private Dictionary<string, Sprite> playerSprites = new();

    // recording
    private bool recording = false;
    private List<GhostFrame> recordingFrames = new();

    public GhostFrame[] CurrentRecording => recordingFrames.ToArray();

    // playback
    private List<GhostPlayback> playbacks = new();

    // config
    private bool pauseStopsTimer = false;

    public void StartRecording(bool pauseStopsTimer = false) {
        recordingFrames.Clear();
        recording = true;
        this.pauseStopsTimer = pauseStopsTimer;
    }

    public void StopRecording() {
        recording = false;
    }

    public void ToggleRecording() {
        if (recording) StopRecording();
        else StartRecording();
    }

    public void ClearGhosts() {
        for (var i = playbacks.Count - 1; i >= 0; i--) {
            var playback = playbacks[i];
            GhostDestroy(playback, i);
        }
    }

    private void GhostDestroy(GhostPlayback playback, int playbacksIndex) {
        Object.Destroy(playback.PlayerCopy);
        playbacks.RemoveAt(playbacksIndex);
    }

    public void Playback(GhostFrame[] frames) {
        var player = Player.i;
        var playerCopy = Object.Instantiate(player.PlayerSprite.gameObject).GetComponent<SpriteRenderer>();
        Object.DontDestroyOnLoad(playerCopy);

        playerCopy.color = ghostColor.Value;

        playbacks.Add(new GhostPlayback(playerCopy, frames, 0));
    }

    private void UpdateRecord() {
        var player = Player.i;
        // we want to use current time since loads can interrupt the stopwatch pause, but if we need to we'll use stopwatch
        var time = speedrunTimerModule.CurrentTime;
        if (time == 0) time = (float)speedrunTimerModule.Stopwatch.Elapsed.TotalSeconds;
        if (!player) return; // TODO?
        if (pauseStopsTimer && RCGTime.timeScale == 0) return;

        recordingFrames.Add(
            new GhostFrame(player.transform.position, player.PlayerSprite.sprite.name, (int)player.Facing, time));
    }

    private void CheckPlayback(GhostPlayback playback, int playbacksIndex = 0) {
        var time = speedrunTimerModule.CurrentTime;
        var stopwatchTime = (float)speedrunTimerModule.Stopwatch.Elapsed.TotalSeconds;
        if (time == 0 || speedrunTimerModule.State is SpeedrunTimerState.InactiveDone or SpeedrunTimerState.Inactive)
            time = stopwatchTime;

        try {
            if (playback.PlaybackIndex < playback.Frames.Length) {
                var ghostFrame = playback.Frames[playback.PlaybackIndex];
                // check if games paused
                if (pauseStopsTimer && RCGTime.timeScale == 0) return;
                else if (ghostFrame.Timestamp < time) {
                    UpdatePlayback(playback, ghostFrame, playbacksIndex);
                } else if (time == 0) {
                    GhostDestroy(playback, playbacksIndex);
                }
            } else {
                GhostDestroy(playback, playbacksIndex);
            }
        } catch (Exception e) {
            Log.Error("Playback Index: " + playback.PlaybackIndex + "Playback Frame Length: " + playback.Frames.Length);
            Log.Error(e);
        }
    }

    private void UpdatePlayback(GhostPlayback playback, GhostFrame ghostFrame, int playbacksIndex) {
        playback.PlayerCopy.transform.position = ghostFrame.Position + Vector3.down * 3.5f;
        playerSprites.TryGetValue(ghostFrame.SpriteName, out var sprite);
        playback.PlayerCopy.sprite = sprite;
        playback.PlayerCopy.transform.localScale = new Vector3(ghostFrame.Facing, 1f, 1f);
        // Increment index when the frame actually plays
        playback.PlaybackIndex++;
        // Loop this until it's caught up to the current time
        CheckPlayback(playback, playbacksIndex);
    }

    public void LateUpdate() {
        if (recording) UpdateRecord();

        for (var i = playbacks.Count - 1; i >= 0; i--) {
            var playback = playbacks[i];

            CheckPlayback(playback, i);
        }

        if (Player.i is { } player) {
            var sprite = player.PlayerSprite.sprite;
            var name = sprite.name;
            playerSprites.TryAdd(name, sprite);
        }
    }

    public void Unload() {
        playbacks.ForEach(playback => Object.Destroy(playback.PlayerCopy));
        playbacks.Clear();
    }
}