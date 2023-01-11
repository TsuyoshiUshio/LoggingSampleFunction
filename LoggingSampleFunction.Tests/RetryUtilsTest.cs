using Microsoft.Extensions.Logging;

namespace LoggingSampleFunction.Tests
{
    public class RetryUtilsTest
    {
        // NOTE: In this case, there is better ways to write this test. 
        // However, this test shows the sample of Asserion by Logging
        [Fact]
        public void TryRetryAndFails()
        {
            var logger = new TestLogger();
            int count = 0;
            var result = RetryUtils.TryRetry("Success after two retry", () =>
            {
                count++;
                if (count > 2)
                {
                    return true;
                }
                return false;
            }, 3, logger);

            Assert.True(result, "Retry fails.");
            Assert.Equal(2, logger.Messages.Count);
            Assert.Contains("Fail to execute Success after two retry currentRetryCount 0 retry until 3", logger.Messages[0].Message);
            Assert.Contains("Fail to execute Success after two retry currentRetryCount 1 retry until 3", logger.Messages[1].Message);
        }
    }
}