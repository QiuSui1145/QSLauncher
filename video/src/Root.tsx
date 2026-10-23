import React from "react";
import { Composition, Still } from "remotion";
import { INTRO_DURATION, QSLauncherIntro } from "./Intro";
import { QSLauncherUpdate120, UPDATE_DURATION } from "./Update120";
import { QSLauncherCover } from "./Cover";
import { VIDEO } from "./theme";

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Still
        id="QSLauncherCover"
        component={QSLauncherCover}
        width={VIDEO.width}
        height={VIDEO.height}
      />
      <Composition
        id="QSLauncherIntro"
        component={QSLauncherIntro}
        durationInFrames={INTRO_DURATION}
        fps={VIDEO.fps}
        width={VIDEO.width}
        height={VIDEO.height}
      />
      <Composition
        id="QSLauncherUpdate120"
        component={QSLauncherUpdate120}
        durationInFrames={UPDATE_DURATION}
        fps={VIDEO.fps}
        width={VIDEO.width}
        height={VIDEO.height}
      />
    </>
  );
};
