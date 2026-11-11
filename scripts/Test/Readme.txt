# Coverage
.\scripts\Test\coverage\coverage.cmd

# Performance: klargøring første gang
.\scripts\Test\Perfomance\setup.cmd

# Efter Windows-genstart: start Docker Desktop, og vent på at motoren er klar
.\scripts\Test\Perfomance\start-aspire.cmd

# Kontroller gatewayen, og kør derefter en lille prøve
.\scripts\Test\Perfomance\check.cmd
.\scripts\Test\Perfomance\smoke.cmd -OpenReport

# Prøve med to browserklienter
.\scripts\Test\Perfomance\browser-smoke.cmd -OpenReport

# Fuld test med 1.285 studerende (100 aktive) og tre undervisere
.\scripts\Test\Perfomance\full.cmd -OpenReport
