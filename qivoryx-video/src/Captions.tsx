import React from 'react';
import {useCurrentFrame, useVideoConfig} from 'remotion';

export type Caption = {text: string; startMs: number; endMs: number};
export const Captions: React.FC<{captions: Caption[]; voiceOffset: number; light?: boolean}> = ({captions, voiceOffset, light = false}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const ms = (frame-voiceOffset) / fps * 1000;
  const active = captions.find((c) => ms >= c.startMs && ms < c.endMs);
  if (!active) return null;
  return <div style={{position: 'absolute', left: 100, right: 100, bottom: 39, minHeight: 68, display: 'flex', justifyContent: 'center', alignItems: 'center', pointerEvents: 'none'}}>
    <div style={{fontFamily: 'Segoe UI, Arial, sans-serif', fontSize: 35, fontWeight: 450, lineHeight: 1.24, textAlign: 'center', maxWidth: 1670, color: light ? '#181818' : '#f7f7f7', background: light ? '#f4f4f0ed' : '#080809ed', borderRadius: 13, padding: '12px 26px', textWrap: 'balance'}}>{active.text}</div>
  </div>;
};
