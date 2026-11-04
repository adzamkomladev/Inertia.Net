# Exploratory measurements before the control delegate correction

These first two pinned runs met the rendering acceptance criteria. The request controls used two separate but equivalent lambda wrappers. Their valid-input latency differed by 37% and 65% despite calling the same parser. The final harness uses the identical Action instance in both control slots to remove that call-site difference. The two final runs and their revised benchmark DLL hash are in the parent directory. These files preserve the initial observations; they are not included in the final summary.
