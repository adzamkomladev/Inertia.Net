package main

import "testing"

func BenchmarkComputeSequential(b *testing.B) {
	for i := 0; i < b.N; i++ {
		computeChecksum(20000, false)
	}
}

func BenchmarkComputeParallel(b *testing.B) {
	for i := 0; i < b.N; i++ {
		computeChecksum(20000, true)
	}
}
