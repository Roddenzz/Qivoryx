import React from 'react';
import {AbsoluteFill, interpolate, Sequence, staticFile, useCurrentFrame} from 'remotion';
import {Audio} from '@remotion/media';
import {TransitionSeries, linearTiming} from '@remotion/transitions';
import {fade} from '@remotion/transitions/fade';
import {Captions} from './Captions';
import {Intro} from './scenes/Intro';
import {Diagnostics} from './scenes/Diagnostics';
import {Services} from './scenes/Services';
import {Power} from './scenes/Power';
import {Graphics} from './scenes/Graphics';
import {FrameLab} from './scenes/FrameLab';
import {Sessions} from './scenes/Sessions';
import {Apps} from './scenes/Apps';
import {Cleanup} from './scenes/Cleanup';
import {Control} from './scenes/Control';
import {Outro} from './scenes/Outro';
import {clamp, cue} from './design';
import timeline from './timeline.json';

export const sceneComponents = [Intro, Diagnostics, Services, Power, Graphics, FrameLab, Sessions, Apps, Cleanup, Control, Outro];

const VoicedScene: React.FC<{index: number}> = ({index}) => {
  const frame = useCurrentFrame();
  const scene = timeline.scenes[index];
  const Component = sceneComponents[index];
  return <AbsoluteFill>
    <Component/>
    <Sequence from={scene.voiceOffset} layout="none"><Audio src={staticFile(scene.audio)} volume={0.95}/></Sequence>
    <Captions captions={scene.captions} voiceOffset={scene.voiceOffset} light={index === 10 && frame < cue('outro', 'ending', 192) + 18}/>
  </AbsoluteFill>;
};

export const Presentation: React.FC = () => {
  const frame = useCurrentFrame();
  return <AbsoluteFill style={{background: '#080809'}}>
    <Audio src={staticFile('score.m4a')} volume={0.7}/>
    <TransitionSeries>
      {timeline.scenes.flatMap((scene, i) => [
        ...(i > 0 ? [<TransitionSeries.Transition key={`transition-${scene.id}`} presentation={fade()} timing={linearTiming({durationInFrames: 18})}/>] : []),
        <TransitionSeries.Sequence key={scene.id} durationInFrames={scene.frames} premountFor={30} name={scene.title}><VoicedScene index={i}/></TransitionSeries.Sequence>,
      ])}
    </TransitionSeries>
    <div style={{position: 'absolute', bottom: 0, left: 0, height: 3, width: `${interpolate(frame, [0, timeline.totalFrames-1], [0, 100], clamp)}%`, background: '#aaa'}}/>
  </AbsoluteFill>;
};
