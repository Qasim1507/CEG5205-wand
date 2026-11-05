"""Send a simulated classifier result to the Unity wand meeting scene."""

import argparse
import json
import socket


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("spell", choices=("fire", "frost", "storm", "arcane", "shatter"))
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5055)
    args = parser.parse_args()
    packet = json.dumps({"spell": args.spell, "conf": 0.93}).encode("utf-8")
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
        sender.sendto(packet, (args.host, args.port))
    print(f"Sent simulated {args.spell} prediction to {args.host}:{args.port}")


if __name__ == "__main__":
    main()
