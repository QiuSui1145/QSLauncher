import React from "react";
import { AbsoluteFill, Sequence, useCurrentFrame } from "remotion";
import { Backdrop } from "./components/Backdrop";
import { Progress } from "./components/Progress";
import { Scene } from "./components/Scene";
import { HeroScene } from "./scenes/HeroScene";
import { PrincipleScene } from "./scenes/PrincipleScene";
import { HomeScene } from "./scenes/HomeScene";
import { VersionsScene } from "./scenes/VersionsScene";
import { ModpacksScene } from "./scenes/ModpacksScene";
import { AppearanceScene } from "./scenes/AppearanceScene";
import { ToolingScene } from "./scenes/ToolingScene";
import { OutroScene } from "./scenes/OutroScene";
import { FONT } from "./theme";

const SCENES = [
  { from: 0, duration: 150, label: "INTRO", render: () => <HeroScene /> },
  { from: 150, duration: 190, label: "OPEN", render: () => <PrincipleScene /> },
  { from: 340, duration: 230, label: "HOME", render: () => <HomeScene /> },
  { from: 570, duration: 230, label: "VERSIONS", render: () => <VersionsScene /> },
  { from: 800, duration: 230, label: "MODPACKS", render: () => <ModpacksScene /> },
  { from: 1030, duration: 220, label: "APPEARANCE", render: () => <AppearanceScene /> },
  { from: 1250, duration: 200, label: "TOOLING", render: () => <ToolingScene /> },
  { from: 1450, duration: 170, label: "OUTRO", render: () => <OutroScene /> },
];

export const INTRO_DURATION = 1620;

export const QSLauncherIntro: React.FC = () => {
  const frame = useCurrentFrame();
  const current =
    [...SCENES].reverse().find((scene) => frame >= scene.from) ?? SCENES[0];

  return (
    <AbsoluteFill style={{ fontFamily: FONT, backgroundColor: "#05080d" }}>
      <Backdrop />
      {SCENES.map((scene) => (
        <Sequence
          key={scene.label}
          from={scene.from}
          durationInFrames={scene.duration}
          name={scene.label}
        >
          <Scene durationInFrames={scene.duration}>{scene.render()}</Scene>
        </Sequence>
      ))}
      <Progress
        section={current.label}
        total={SCENES.length}
        index={SCENES.findIndex((scene) => scene.label === current.label) + 1}
      />
    </AbsoluteFill>
  );
};
