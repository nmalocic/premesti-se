"""Reject missing, empty, skipped-only, or failing unit-test reports."""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def verify(path: Path) -> None:
    root = ET.parse(path).getroot()
    if path.suffix == ".trx":
        counters = root.find("{*}ResultSummary/{*}Counters")
        if counters is None:
            raise ValueError("TRX report has no result counters")
        executed = int(counters.attrib["executed"])
        passed = int(counters.attrib["passed"])
        failed = int(counters.attrib["failed"])
        successful = root.find("{*}ResultSummary").attrib["outcome"] == "Completed"
    else:
        cases = list(root.iter("testcase"))
        executed = sum(case.find("skipped") is None for case in cases)
        failed = sum(
            case.find("failure") is not None or case.find("error") is not None
            for case in cases
        )
        passed = executed - failed
        successful = failed == 0

    if executed <= 0 or passed <= 0 or failed != 0 or not successful:
        raise ValueError(
            f"Expected successful executed unit tests: executed={executed}, "
            f"passed={passed}, failed={failed}"
        )
    print(f"Verified {executed} executed unit tests in {path}")


if __name__ == "__main__":
    try:
        verify(Path(sys.argv[1]))
    except (IndexError, OSError, ET.ParseError, KeyError, ValueError) as error:
        sys.exit(f"Unit-test report verification failed: {error}")
