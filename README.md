# KHPCSoundTools
Music Tracks Locations: https://docs.google.com/spreadsheets/d/1JMAhUSeEf3r-njF2-8EBX8mUDVa0xaLs/edit#gid=1851343023
#### Multi Encoder:
Converts WAV files to Kingdom Hearts PC sound format <br/>
Compatible with any SCD
##### Usage:
MultiEncoder <InputSCD/Dir> <br/>
Put the WAVs in the multiencoder folder <br/>
Rename them starting from 1.wav until you fulfil the number of tracks <br/>
#### Single Encoder:
Converts WAV files to Kingdom Hearts PC sound format <br/>
Compatible only with single-tracked SCDs
##### Usage:
SingleEncoder <InputSCD/Dir> <InputWAV/Dir> <br/>
#### How to Input Loops:
##### Full Loop:
WAV must have LoopStart and LoopEnd tags <br/>
LoopStart must be equal to 0 <br/>
LoopEnd must be equal to the last sample of the WAV.
##### Custom Loop:
WAV must have LoopStart and LoopEnd tags <br/>
LoopEnd must be equal to the last sample of the WAV.
##### No Loop:
WAV must not have LoopStart and LoopEnd tags.
#### SCD Info:
Shows data from SCD files, decrypts them and extracts sound files from them.
##### Usage:
SCDInfo info|extract|decrypt File/Dir
