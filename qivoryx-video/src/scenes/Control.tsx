import React from 'react';
import {cue, Copy, Screen, Stage, Tag} from '../design';

export const Control: React.FC = () => <Stage index={10} name="ТОНКАЯ НАСТРОЙКА">
  <Copy eyebrow="08 / ПОНИМАЙТЕ ИЗМЕНЕНИЯ" title={'Ваши\nнастройки.\nС историей.'} detail="Точечные изменения с резервными копиями параметров и журналом." chips={['Меню', 'Мышь', 'Запись', 'Оформление']} size={78}/>
  <Screen file="dark-11" crop={{x: 250, y: 115, w: 1150, h: 720}}/>
  <Tag text="Поддерживаемые настройки обратимы" x={951} y={845} delay={cue('control', 'tag', 215)}/>
</Stage>;
