# Parkour v2 version contract

Live protocol 78 selects Parkour profile v2 and its current-contact wallstrafe
policy. Protocol 77 recordings retain Parkour v1. Earlier protocols cannot
select Parkour. Classic remains the default; Parkour is an explicit practice
and multiplayer host choice.

Packet IDs and widths, modifier bit 19, the protocol-77 tuck and pivot-counter
boundaries, and historical registry fixtures remain unchanged. Explicit discovery,
metadata and checkpoint allowlists include protocol 78. Pre-77 reserved-bit
normalization remains independent of current profile selection. Passive replicas
do not rerun the active wallstrafe controller.

[Parkour guide](../movement/parkour-movement.md) describes controls and limits.
Windows validation and its exact source/binary evidence will be recorded with
the completed wallstrafe slice.
