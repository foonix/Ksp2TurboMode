using KSP.Sim.ResourceSystem;
using NUnit.Framework;
using TurboMode.Models;

namespace TurboMode.Tests
{

    public class ResourceAmountsTests
    {
        static readonly ResourceDefinitionID methane = new(12);
        static readonly ContainedResourceData methaneTankSpec = new()
        {
            ResourceID = new ResourceDefinitionID(12),
            CapacityUnits = 10,
            StoredUnits = 7,
        };

        [Test]
        public void AddsResourcesClampedByStorageAmount()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            var origRet = rc.AddResourceUnits(methane, 100);
            var tmRet = ra.AddResourceUnits(100);

            Assert.AreEqual(origRet, tmRet);
            Assert.AreEqual(3d, tmRet);
            Assert.AreEqual(10d, ra.stored);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
        }

        [Test]
        public void RemovesResourcesClampedByZero()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            var origRet = rc.RemoveResourceUnits(methane, 100);
            var tmRet = ra.RemoveResourceUnits(100);

            Assert.AreEqual(origRet, tmRet);
            Assert.AreEqual(7d, tmRet);
            Assert.AreEqual(0d, ra.stored);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
        }

        [Test]
        public void FillsToCapacity()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            var origRet = rc.FillResourceToCapacity(methane);
            var tmRet = ra.FillResourceToCapacity();

            Assert.AreEqual(origRet, tmRet);
            Assert.AreEqual(3d, tmRet);
            Assert.AreEqual(10d, ra.stored);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
        }

        [Test]
        public void ConsumesPreprocessed()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            var origRet = rc.ConsumePreProcessedResourceUnits(methane, 20);
            var tmRet = ra.ConsumePreProcessedResourceUnits(20);

            Assert.AreEqual(origRet, tmRet);
            Assert.AreEqual(7d, tmRet);
            Assert.AreEqual(7d, ra.stored);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
            Assert.AreEqual(rc.GetResourcePreProcessedUnits(methane), ra.preProcessed);
        }

        [Test]
        public void StoresPreprocessed()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            var origRet = rc.StorePreProcessedResourceUnits(methane, 20);
            var tmRet = ra.StorePreProcessedResourceUnits(20);

            Assert.AreEqual(origRet, tmRet);
            Assert.AreEqual(3d, tmRet);
            Assert.AreEqual(7d, ra.stored);
            Assert.AreEqual(-3d, rc.GetResourcePreProcessedUnits(methane));
            Assert.AreEqual(-3d, ra.preProcessed);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
            Assert.AreEqual(rc.GetResourcePreProcessedUnits(methane), ra.preProcessed);
        }

        [Test]
        public void ResetsPreprocessed()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            rc.StorePreProcessedResourceUnits(methane, 20);
            ra.StorePreProcessedResourceUnits(20);
            Assert.AreEqual(-3d, rc.GetResourcePreProcessedUnits(methane));
            Assert.AreEqual(-3d, ra.preProcessed);

            rc.ResetPreProcessedResources();
            ra.ResetPreProcessedResources();

            Assert.AreEqual(0d, ra.preProcessed);
            Assert.AreEqual(7d, ra.stored);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
            Assert.AreEqual(rc.GetResourcePreProcessedUnits(methane), ra.preProcessed);
        }

        [Test]
        public void DumpsPreprocessed()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            rc.StorePreProcessedResourceUnits(methane, 20);
            ra.StorePreProcessedResourceUnits(20);
            var origRet = rc.DumpPreProcessedResource(methane);
            var tmRet = ra.DumpPreProcessedResource();

            Assert.AreEqual(origRet, tmRet);
            Assert.AreEqual(10d, tmRet);
            Assert.AreEqual(7d, ra.stored);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
            Assert.AreEqual(rc.GetResourcePreProcessedUnits(methane), ra.preProcessed);
        }

        [Test]
        public void ReturnsCorrectEmptyUnits()
        {
            var rc = new ResourceContainer(methaneTankSpec);
            var ra = new ResourceAmounts(methaneTankSpec);

            rc.StorePreProcessedResourceUnits(methane, 20);
            ra.StorePreProcessedResourceUnits(20);
            rc.ConsumePreProcessedResourceUnits(methane, 4);
            ra.ConsumePreProcessedResourceUnits(4);
            var rcEmpty = rc.GetResourceEmptyUnits(methane);
            var raEmpty = ra.GetResourceEmptyUnits();
            var rcEmptyWithPreprocessed = rc.GetResourceEmptyUnits(methane, true);
            var raEmptyWithPreprocessed = ra.GetResourceEmptyUnits(true);


            Assert.AreEqual(rcEmpty, raEmpty);
            Assert.AreEqual(3d, raEmpty);
            Assert.AreEqual(7d, ra.stored);
            Assert.AreEqual(rcEmpty, raEmpty);
            Assert.AreEqual(rcEmptyWithPreprocessed, raEmptyWithPreprocessed);
            Assert.AreEqual(rc.GetResourceStoredUnits(methane), ra.stored);
            Assert.AreEqual(rc.GetResourcePreProcessedUnits(methane), ra.preProcessed);
        }
    }
}
