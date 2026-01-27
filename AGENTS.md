AGENTS.md

This project replaces the removed SteelSeries System Monitor App. It uses the LibreHardwareMonitorLib to gather system information and display them on various SteelSeries OLED displays like Keyboards or Headphone stations.

## Runtime environment (for AI agents)

- Primary dev OS: Windows 11.
- Default shell: PowerShell 7.

## Expectations
- Keep changes small and consistent with the existing architecture.
- Update `docs/` and relevant `README.md` files whenever you change behaviour or add features.
- Update CI workflow after relevant changes

## DOs
- ALWAYS make sure to stick with security best practises
- This software runs on gamer computers. ALWAYS consider application performance as a top priority, to not have influence on running games. 

## DONTs
- NEVER touch files in `gamesense-sdk/`. We only add it as a submodule for documentation reference. 

## Knowledgebase
- `README.md` for general information about this application
- `gamesense-sdk/` for all information regarding 
