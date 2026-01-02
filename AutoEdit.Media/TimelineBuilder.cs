using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoEdit.Media;

public sealed class TimelineBuilder
{
    public List<TimelineEvent> Build(
        AudioAnalysisResult audio,
        List<VideoAnalysisResult> videos,
        double minClipDuration,
        double maxClipDuration,
        double aggressiveness) // 0-100, hur ofta vi klipper på beats
    {
        var timeline = new List<TimelineEvent>();
        
        if (videos.Count == 0) return timeline;

        var random = new Random();
        double currentTimelineTime = 0;
        
        // Håll koll på hur mycket vi använt av varje klipp för att undvika upprepning direkt
        var clipCursors = videos.ToDictionary(v => v, v => 0.0);
        
        // Sortera beats
        var beatTimes = audio.BeatTimes.ToList();
        
        // Lägg till slutet av låten som en "beat" om det saknas
        if (beatTimes.Count == 0 || beatTimes.Last() < audio.DurationSeconds)
            beatTimes.Add(audio.DurationSeconds);

        int beatIndex = 0;
        
        // Normalisera aggressiveness till 0.0-1.0
        // Hög aggressiveness = klipp på fler beats, kortare klipp
        // Låg aggressiveness = hoppa över beats, längre klipp
        double aggNorm = Math.Clamp(aggressiveness / 100.0, 0.0, 1.0);
        
        // Sannolikhet att klippa på en giltig beat (högre agg = högre sannolikhet)
        double beatCutProbability = 0.3 + (aggNorm * 0.7); // 30% till 100%

        // Loopa tills vi fyllt musiken eller slut på video (men vi loopar video om det behövs)
        while (currentTimelineTime < audio.DurationSeconds)
        {
            // 1. Bestäm längd på nästa klipp
            // Hitta nästa beat som ligger minst minClipDuration bort
            double targetTime = currentTimelineTime + minClipDuration;
            
            // Hitta närmaste beat efter targetTime
            // Om aggressiveness är hög, försök träffa exakt på beats ofta.
            // Om låg, kanske vi hoppar över några beats.
            
            // Hitta nästa lämpliga beat
            double nextCutTime = -1;
            
            for (int i = beatIndex; i < beatTimes.Count; i++)
            {
                double t = beatTimes[i];
                double duration = t - currentTimelineTime;
                
                if (duration >= minClipDuration)
                {
                    if (duration <= maxClipDuration)
                    {
                        // Använd aggressiveness för att avgöra om vi ska klippa här
                        // eller hoppa till nästa beat för ett längre klipp
                        bool shouldCutHere = random.NextDouble() < beatCutProbability;
                        
                        if (shouldCutHere || duration >= maxClipDuration * 0.8)
                        {
                            // Klipp på denna beat
                            nextCutTime = t;
                            beatIndex = i + 1;
                            break;
                        }
                        // Annars fortsätt leta efter nästa beat (längre klipp)
                    }
                    else
                    {
                        // Om nästa beat är för långt bort, måste vi klippa tidigare (utanför beat) eller ta detta beat ändå?
                        // Vi tar maxClipDuration om ingen beat passar
                        nextCutTime = currentTimelineTime + maxClipDuration;
                        // Justera beatIndex så vi inte missar beats som kommer efter detta artificiella klipp
                        while (beatIndex < beatTimes.Count && beatTimes[beatIndex] < nextCutTime)
                            beatIndex++;
                        break;
                    }
                }
            }

            if (nextCutTime < 0)
            {
                // Inga fler beats eller beats för långt bort?
                // Klipp till slutet av låten eller max length
                double remaining = audio.DurationSeconds - currentTimelineTime;
                nextCutTime = currentTimelineTime + Math.Min(remaining, maxClipDuration);
            }

            double clipDuration = nextCutTime - currentTimelineTime;
            
            // 2. Välj videoklipp (enkelt: slumpa, men försök inte ta samma som nyss)
            var availableVideos = videos.Where(v => v != timeline.LastOrDefault()?.SourceFilePath as object).ToList(); // Lite ful check, men funkar om vi hade objekten.
            // Bättre: bara slumpa från listan, se till att det inte är samma index som förra om count > 1.
            
            VideoAnalysisResult selectedVideo;
            if (videos.Count == 1)
            {
                selectedVideo = videos[0];
            }
            else
            {
                // Undvik samma som sist
                var lastSource = timeline.LastOrDefault()?.SourceFilePath;
                var candidates = videos.Where(v => v.FilePath != lastSource).ToList();
                if (candidates.Count == 0) candidates = videos; // Borde inte hända om > 1
                selectedVideo = candidates[random.Next(candidates.Count)];
            }

            // 3. Välj starttid i videon
            // Försök hitta en scengräns som ligger nära nuvarande cursor för detta klipp?
            // Eller bara fortsätt där vi var + lite hopp.
            
            double sourceStart = clipCursors[selectedVideo];
            
            // Om vi är för nära slutet av videon, börja om eller hitta scengräns
            if (sourceStart + clipDuration > selectedVideo.DurationSeconds)
            {
                sourceStart = 0;
                // Försök hitta en scengräns tidigt i filen
                var firstScene = selectedVideo.SceneChanges.FirstOrDefault(t => t > 5.0 && t < selectedVideo.DurationSeconds - clipDuration);
                if (firstScene > 0) sourceStart = firstScene;
            }
            else
            {
                // Kolla om det finns en scengräns i närheten framåt att hoppa till för mer "action"?
                // Enkelt nu: Linjärt med lite slumpmässigt hopp ibland
                if (random.NextDouble() > 0.7)
                {
                    // Hoppa framåt 2-10 sekunder för variation
                    sourceStart += 2.0 + random.NextDouble() * 8.0;
                    if (sourceStart + clipDuration > selectedVideo.DurationSeconds) sourceStart = 0;
                }
            }
            
            // Uppdatera cursor
            clipCursors[selectedVideo] = sourceStart + clipDuration;

            timeline.Add(new TimelineEvent
            {
                SourceFilePath = selectedVideo.FilePath,
                SourceStart = sourceStart,
                Duration = clipDuration,
                TimelineStart = currentTimelineTime
            });

            currentTimelineTime += clipDuration;
            
            // Säkerhetsbreak om vi fastnar (t.ex. clipDuration blir 0)
            if (clipDuration <= 0.001) currentTimelineTime += 0.1; 
        }

        return timeline;
    }
}
