# Feature Specification: 1v1 Asymmetric Multiplayer Gameplay & Balance Rules

**Document ID:** `specs/multiplayer-gameplay.md`  
**Status:** Approved / Draft  
**Target System:** 1v1 Web-Based Pac-Man (Player-vs-Player)  
**Author:** Đại Đại team

---

## 1. Overview & System Goals

Standard single-player Pac-Man relies on deterministic AI behavior where ghosts follow rigid pathfinding routines (Chase, Scatter, Frightened). In a 1v1 human-versus-human environment, a human-controlled Ghost presents an inherently higher difficulty curve due to non-deterministic tactics (e.g., cutting off escape routes, camping power pellets, and waiting at intersections).

This specification establishes asymmetric movement speeds, vision systems, power pellet debuffs, anti-camping heuristics, and match scoring mechanics to guarantee a fair, competitive balance between **Pac-Man (Runner)** and the **Human Ghost (Hunter)**.

---

## 2. Role Capabilities & Speed Differentials

To counter tactical trapping by the Ghost player, Pac-Man maintains a constant velocity advantage and superior turn responsiveness.

### 2.1 Character Specifications

| Metric / Attribute | Pac-Man (Runner) | Ghost (Hunter) |
| :--- | :--- | :--- |
| **Primary Objective** | Clear $100\%$ of pellets or survive match timer | Eliminate Pac-Man 3 times |
| **Base Movement Speed** | **$100\%$** ($1.00\times$ grid unit speed) | **$95\%$** ($0.95\times$ grid unit speed) |
| **Cornering Penalty** | $0\%$ speed loss on directional queuing | $5\%$ speed deceleration if turning off-grid center |
| **Lives / Respawns** | 3 Lives (Match ends at 0) | Unlimited Respawns (5s delay when eaten) |
| **Collision Box** | $0.8\times 0.8$ tile radius | $0.8\times 0.8$ tile radius |

### 2.2 Movement Mechanics & Pathing
* **Pac-Man Cornering:** Pac-Man benefits from pre-buffered cornering. When inputting a turn prior to reaching a grid intersection, Pac-Man snaps cleanly onto the new axis without speed degradation.
* **Ghost Pursuit:** Because Ghost velocity is set to $95\%$ of Pac-Man's speed, the Ghost cannot win via a direct linear tail-chase. Victory requires predicting Pac-Man’s movement path, utilizing map shortcuts, and positioning at upcoming intersections.

---

## 3. Power Pellet Mechanics & Frightened State

Power Pellets temporarily reverse the roles of hunter and hunted. Upon consumption of a Power Pellet by Pac-Man, the game enters the **Frightened State** for a duration of **8.0 seconds**.

```
+-----------------------------------------------------------------------+
|                             NORMAL STATE                              |
|   Ghost (95% Speed)  --- Hunts --->  Pac-Man (100% Speed)            |
+-----------------------------------------------------------------------+
                                   |
                     [Pac-Man eats Power Pellet]
                                   |
                                   v
+-----------------------------------------------------------------------+
|                           FRIGHTENED STATE                            |
|   Pac-Man (100% Speed)  --- Hunts --->  Ghost (70% Speed + Inverted) |
+-----------------------------------------------------------------------+
```

### 3.1 Frightened State Parameters
* **Duration:** 8.0 seconds (Non-stackable; eating a second Power Pellet resets the timer to 8.0s).
* **Ghost Movement Penalty:** Ghost speed drops from $95\%$ to **$70\%$** ($0.70\times$ base grid speed).
* **Control Disorientation:** During the initial 3.0 seconds of Frightened State, the Ghost player's directional inputs are inverted (Up $\rightarrow$ Down, Left $\rightarrow$ Right).
* **Visual Identifiers:**
  * Ghost sprite shifts to flashing blue/white.
  * Ghost UI overlay displays a vignetted blue pulse and countdown timer.

### 3.2 Ghost Eaten & Respawn Sequence
1. Upon contact during Frightened State, Pac-Man gains bonus points ($+200 / +400 / +800 / +1600\text{ pts}$ progressive multiplier).
2. Ghost is reduced to "Eyes Only" mode and moves at **$150\%$ speed** back to the Center Ghost House.
3. Upon reaching the Ghost House, a **5.0-second lockout timer** is enforced before the Ghost is re-released into active play.

---

## 4. Vision Mechanics & Anti-Camping Protocols

### 4.1 Fog of War (Ghost Line-of-Sight)
To prevent the Ghost from having global omniscient map vision and instantly predicting all escapes, asymmetric vision rules apply:
* **Pac-Man Vision:** Full global map visibility ($100\%$ map revealed at all times).
* **Ghost Vision (Fog of War):**
  * **Direct Radius:** Full visibility within a **6-tile radius** around the Ghost.
  * **Line of Sight (LOS):** Full visibility down unobstructed linear corridors/hallways.
  * **Sonar Pulse:** If Pac-Man is outside the 6-tile radius and behind walls, a subtle sonar ring emits on the Ghost's HUD every **4.0 seconds**, indicating Pac-Man's approximate quadrant.

### 4.2 Anti-Camping Soft-Wall Heuristic
To prevent the Ghost player from idling over uncollected Power Pellets or bottlenecking critical intersections:
* **Zone Trigger:** If the Ghost remains within a 3-tile radius of an uncollected Power Pellet for $> 5.0$ continuous seconds without active chase engagement:
* **Penalty Enforcement:** The Ghost receives an **Anti-Camping Debuff**, reducing movement speed by an additional $15\%$ ($80\%$ net base speed) and granting Pac-Man a speed boost indicator on HUD until the Ghost exits the zone.

---

## 5. Win / Loss Conditions & Match Structure

Matches operate under a **3-minute (180-second) strict countdown clock**.

```
                          +-------------------------------+
                          |    1v1 Match Loop (3:00)      |
                          +-------------------------------+
                                          |
        +---------------------------------+---------------------------------+
        |                                 |                                 |
        v                                 v                                 v
[Pac-Man Clears 100% Dots]      [Ghost Eats Pac-Man 3x]         [Match Timer Reaches 0:00]
        |                                 |                                 |
        v                                 v                                 v
  PAC-MAN VICTORY                   GHOST VICTORY                [Evaluate Score Matrix]
                                                                            |
                                                    +-----------------------+-----------------------+
                                                    |                                               |
                                                    v                                               v
                                      (Pac-Man Dots >= 70%)                           (Pac-Man Dots < 70%)
                                                    |                                               |
                                                    v                                               v
                                             PAC-MAN VICTORY                                  GHOST VICTORY
```

### 5.1 Victory Pathways

1. **Pac-Man Instant Victory:**
   * Collects $100\%$ of pellets/dots on the active map before the 3:00 timer expires.
2. **Ghost Instant Victory:**
   * Reduces Pac-Man's life counter from 3 to 0 via physical contact in normal state.
3. **Time-Out Evaluation (Timer = 0:00):**
   * **Pac-Man Victory:** Pac-Man has successfully cleared $\ge 70\%$ of total map dots and possesses a higher score than the Ghost benchmark.
   * **Ghost Victory:** Pac-Man failed to collect at least $70\%$ of total map dots.

---

## 6. Scoring Matrix

Scores are tracked in real-time on the synchronized HUD:

| Event / Action | Point Value | Recipient |
| :--- | :--- | :--- |
| Regular Pellet Collected | $+10\text{ pts}$ | Pac-Man |
| Power Pellet Collected | $+50\text{ pts}$ | Pac-Man |
| Ghost Eaten (1st in chain) | $+200\text{ pts}$ | Pac-Man |
| Ghost Eaten (2nd in chain) | $+400\text{ pts}$ | Pac-Man |
| Ghost Eaten (3rd in chain) | $+800\text{ pts}$ | Pac-Man |
| Ghost Eaten (4th in chain) | $+1600\text{ pts}$ | Pac-Man |
| Ghost Eliminates Pac-Man | $+500\text{ pts}$ | Ghost |
| Time Bonus (per sec remaining) | $+5\text{ pts}$ | Pac-Man (if $100\%$ dots cleared) |

---