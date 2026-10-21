import React from 'react';
import {AbsoluteFill, CanvasImage, Easing, Interactive, interpolate, staticFile, useCurrentFrame} from 'remotion';
import timeline from './timeline.json';

export const cue = (sceneId: string, name: string, fallback: number) =>
  (timeline.scenes.find((scene) => scene.id === sceneId)?.cues as Record<string, number> | undefined)?.[name] ?? fallback;

export const ease = Easing.bezier(0.16, 1, 0.3, 1);
export const clamp = {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'} as const;
export const enter = (frame: number, delay = 0, duration = 32) => interpolate(frame, [delay, delay + duration], [0, 1], {...clamp, easing: ease});

export const QMark: React.FC<{size?: number; light?: boolean}> = ({size = 60, light = false}) => (
  <svg width={size} height={size} viewBox="0 0 120 120" fill="none">
    <circle cx="56" cy="54" r="35" stroke={light ? '#111' : '#fff'} strokeWidth="13" />
    <path d="M68 68 L104 104" stroke={light ? '#111' : '#fff'} strokeWidth="13" strokeLinecap="round" />
  </svg>
);

export const Stage: React.FC<{children: React.ReactNode; index: number; name: string; light?: boolean}> = ({children, index, name, light = false}) => {
  const frame = useCurrentFrame();
  return <AbsoluteFill style={{background: light ? '#f3f3f0' : '#080809', color: light ? '#111' : '#f4f4f4', fontFamily: 'Segoe UI, Arial, sans-serif', overflow: 'hidden'}}>
    <AbsoluteFill style={{background: light ? 'radial-gradient(ellipse at 70% 35%, #ffffff 0%, transparent 65%)' : 'radial-gradient(ellipse at 74% 45%, #242426 0%, transparent 67%)'}} />
    <div style={{position: 'absolute', inset: 0, opacity: light ? 0.06 : 0.045, backgroundImage: 'linear-gradient(#777 1px, transparent 1px), linear-gradient(90deg, #777 1px, transparent 1px)', backgroundSize: '120px 120px', maskImage: 'linear-gradient(transparent, black, transparent)', translate: `${interpolate(frame, [0, 700], [0, -22], clamp)}px 0px`}} />
    <div style={{position: 'absolute', top: 57, left: 88, right: 88, height: 50, display: 'flex', alignItems: 'center', justifyContent: 'space-between'}}>
      <div style={{display: 'flex', alignItems: 'center', gap: 16}}><QMark size={42} light={light}/><span style={{fontSize: 27, fontWeight: 650, letterSpacing: 5}}>QIVORYX</span></div>
      <div style={{display: 'flex', alignItems: 'center', gap: 30, color: light ? '#666' : '#999', fontSize: 22, letterSpacing: 2}}><span>{name}</span><span style={{fontVariantNumeric: 'tabular-nums'}}>{String(index).padStart(2, '0')} / 11</span></div>
    </div>
    {children}
  </AbsoluteFill>;
};

export const Copy: React.FC<{eyebrow: string; title: string; detail: string; chips?: string[]; size?: number}> = ({eyebrow, title, detail, chips = [], size = 78}) => {
  const frame = useCurrentFrame();
  return <div style={{position: 'absolute', top: 202, left: 94, width: 570}}>
    <Interactive.Div name="Раздел" style={{fontSize: 24, letterSpacing: 3, color: '#a8a8ae', opacity: interpolate(frame, [0, 24], [0, 1], clamp), translate: interpolate(frame, [0, 32], ['0px 20px', '0px 0px'], {...clamp, easing: ease})}}>{eyebrow}</Interactive.Div>
    <Interactive.Div name="Заголовок" style={{fontSize: size, lineHeight: 1.07, fontWeight: 620, letterSpacing: -3.4, whiteSpace: 'pre-line', marginTop: 28, opacity: interpolate(frame, [5, 35], [0, 1], clamp), translate: interpolate(frame, [5, 42], ['0px 42px', '0px 0px'], {...clamp, easing: ease})}}>{title}</Interactive.Div>
    <Interactive.Div name="Описание" style={{fontSize: 34, lineHeight: 1.35, color: '#b7b7bc', marginTop: 34, maxWidth: 520, opacity: interpolate(frame, [15, 50], [0, 1], clamp)}}>{detail}</Interactive.Div>
    <div style={{display: 'flex', gap: 12, flexWrap: 'wrap', marginTop: 38}}>{chips.map((chip, i) => <div key={chip} style={{border: '1px solid #49494d', borderRadius: 30, padding: '10px 20px', fontSize: 23, color: '#e2e2e4', opacity: enter(frame, 28 + i * 8), translate: `0px ${(1-enter(frame, 28+i*8))*14}px`}}>{chip}</div>)}</div>
  </div>;
};

type Crop = {x: number; y: number; w: number; h: number};
export const Screen: React.FC<{file: string; x?: number; y?: number; w?: number; h?: number; crop?: Crop; delay?: number; label?: string; zoom?: number; light?: boolean}> = ({file, x = 724, y = 214, w = 1104, h = 688, crop = {x: 0, y: 0, w: 1440, h: 940}, delay = 10, label, zoom = 1.025, light = false}) => {
  const frame = useCurrentFrame();
  const scale = Math.max(w/crop.w, h/crop.h);
  const offsetX = (w-crop.w*scale)/2;
  const offsetY = (h-crop.h*scale)/2;
  return <div style={{position: 'absolute', left: x, top: y, width: w, height: h, borderRadius: 23, border: light ? '1px solid #c9c9c9' : '1px solid #56565a', background: '#101010', overflow: 'hidden', boxShadow: light ? '0 28px 80px #0002' : '0 45px 100px #0009, 0 1px 1px #ffffff30 inset', opacity: enter(frame, delay), translate: `0px ${(1-enter(frame, delay, 42))*60}px`, rotate: `${interpolate(frame, [delay, delay+55], [0.8, 0], {...clamp, easing: ease})}deg`}}>
    <div style={{position: 'absolute', inset: 0, scale: interpolate(frame, [delay+30, delay+500], [1, zoom], clamp), transformOrigin: '50% 40%'}}>
      <CanvasImage name={`Экран — ${file}`} src={staticFile(`screens/${file}.png`)} style={{position: 'absolute', left: offsetX-crop.x*scale, top: offsetY-crop.y*scale, width: 1440*scale, height: 940*scale}} />
    </div>
    {label ? <div style={{position: 'absolute', left: 22, top: 20, padding: '9px 15px', background: '#f4f4f4', color: '#161616', fontSize: 22, fontWeight: 650, borderRadius: 8, boxShadow: '0 4px 24px #0005'}}>{label}</div> : null}
  </div>;
};

export const Crossfade: React.FC<{at: number; children: [React.ReactNode, React.ReactNode]; duration?: number}> = ({at, children, duration = 26}) => {
  const frame = useCurrentFrame();
  const t = interpolate(frame, [at, at+duration], [0, 1], {...clamp, easing: Easing.inOut(Easing.cubic)});
  return <><AbsoluteFill style={{opacity: 1-t}}>{children[0]}</AbsoluteFill><AbsoluteFill style={{opacity: t}}>{children[1]}</AbsoluteFill></>;
};

export const Tag: React.FC<{text: string; x: number; y: number; delay?: number}> = ({text, x, y, delay = 60}) => {
  const frame = useCurrentFrame();
  return <div style={{position: 'absolute', left: x, top: y, opacity: enter(frame, delay), translate: `0px ${(1-enter(frame, delay))*15}px`, background: '#f0f0f0', color: '#121212', borderRadius: 15, padding: '17px 25px', fontSize: 28, fontWeight: 600, boxShadow: '0 10px 40px #0005', display: 'flex', gap: 14, alignItems: 'center'}}><span style={{width: 9, height: 9, background: '#151515', borderRadius: 5}}/>{text}</div>;
};
